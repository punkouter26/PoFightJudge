using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using PoFightJudge.Api.Features.Fight;
using PoFightJudge.Api.Features.Live;
using PoFightJudge.Api.Features.Storage;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;
using PoFightJudge.TestSupport;

namespace PoFightJudge.Unit.Fight;

/// <summary>
/// Runs one live fight: audio both ways, the host's tool calls applied to the rules, deterministic nudges, two
/// recorded tracks, and everything persisted when it ends.
/// </summary>
public sealed class DebateOrchestratorTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 9, 6, 19, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Start);
    private readonly FakeGeminiLiveClientFactory _clients = new();
    private readonly RecordingLiveSink _sink = new();
    private readonly RecordingAnalysisIntake _analysis = new();
    private readonly InMemoryAudioBlobStore _blobs = new();
    private readonly InMemoryWatchResultRepository _watchResults = new();
    private readonly InMemoryFighterResultRepository _fighterResults = new();
    private readonly InMemoryMatchRepository _matches;
    private readonly DebateOptions _options = new()
    {
        MaxDebateSeconds = 180,
        LongTalkerSeconds = 45,
        SilenceSeconds = 10,
        MaxSessionSeconds = 900,
        ClientGraceSeconds = 30,
        VerdictGraceSeconds = 45,
        SilenceGraceMilliseconds = 800,
        SilenceKeepAliveEvery = 4,
    };

    private DebateOrchestrator? _orchestrator;

    public DebateOrchestratorTests() => _matches = new InMemoryMatchRepository(_watchResults, _fighterResults);

    private static byte[] Loud(int samples = 800)
    {
        var pcm = new byte[samples * 2];
        for (var i = 0; i < samples; i++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2), (short)(i % 2 == 0 ? 12000 : -12000));
        }

        return pcm;
    }

    private static byte[] Quiet(int samples = 800) => new byte[samples * 2];

    private DebateOrchestrator New(ShowSetup? setup = null, bool isFake = false)
    {
        _orchestrator = new DebateOrchestrator(
            "user-1",
            MatchId.New(),
            setup ?? new ShowSetup(HostPersonaId.Referee, "AB", "CD", "the thermostat"),
            () => _clients.Create(),
            _sink,
            _matches,
            _blobs,
            _analysis,
            _options,
            _clock,
            isFake,
            NullLogger<DebateOrchestrator>.Instance);
        return _orchestrator;
    }

    /// <summary>Starts a fight with a browser attached, which is the only state in which one runs.</summary>
    private async Task<DebateOrchestrator> StartedAsync(ShowSetup? setup = null, bool isFake = false)
    {
        var orchestrator = New(setup, isFake);
        orchestrator.ClientConnected();
        await orchestrator.StartAsync(CancellationToken.None);
        return orchestrator;
    }

    /// <summary>Starts a fight and takes it as far as the first turn, which is where most of these tests begin.</summary>
    private async Task<DebateOrchestrator> DebatingAsync(string floor = "player1")
    {
        var orchestrator = await StartedAsync();
        await EmitAsync(
            Call(ToolDeclarations.SetPlayers, new { topic = "the thermostat", player1 = "Alex", player2 = "Sam" }),
            () => orchestrator.Session.Phase == SessionPhase.Setup);
        await EmitAsync(
            Call(ToolDeclarations.StartTurn, new { player = floor }),
            () => orchestrator.Session.Phase == SessionPhase.Debate);
        return orchestrator;
    }

    /// <summary>Pushes an event in and waits for the orchestrator's loop to have acted on it.</summary>
    private const int PollAttempts = 400;

    private async Task EmitAsync(LiveServerEvent evt, Func<bool> until)
    {
        _clients.Latest.Emit(evt);
        await WaitForAsync(until);
        until().Should().BeTrue($"the orchestrator should have handled the event (errors: {string.Join("; ", _sink.Errors)})");
    }

    /// <summary>The event loop runs on its own thread, so a test waits for it rather than assuming it has caught up.</summary>
    private static async Task WaitForAsync(Func<bool> until)
    {
        for (var attempt = 0; attempt < PollAttempts && !until(); attempt++)
        {
            await Task.Delay(5, CancellationToken.None);
        }
    }

    private static LiveServerEvent.ToolCall Call(string name, object args) =>
        new("fc-1", name, JsonSerializer.SerializeToElement(args));

    public async ValueTask DisposeAsync()
    {
        if (_orchestrator is not null)
        {
            await _orchestrator.DisposeAsync();
        }
    }

    [Fact]
    public async Task Starting_records_the_match_as_live_and_tells_the_host_who_it_is()
    {
        var orchestrator = await StartedAsync();

        var stored = await _matches.GetAsync("user-1", orchestrator.Session.MatchId, CancellationToken.None);
        stored.Should().NotBeNull();
        stored!.Status.Should().Be(SessionStatus.Live);
        stored.Mode.Should().Be(MatchMode.Fight);
        stored.Side1.Id.Should().Be("AB");
        stored.Side2.IsHuman.Should().BeTrue("a fight is two people on one microphone");

        var config = _clients.Latest.Config;
        config.Should().NotBeNull();
        config!.SystemInstruction.Should().Contain("\"AB\"");
        config.Voice.Should().Be(HostPersona.VoiceFor(HostPersonaId.Referee));
        config.FunctionDeclarations.Should().NotBeNull("the host cannot drive the show without its tools");
        _sink.Latest.Should().NotBeNull();
    }

    [Fact]
    public async Task The_show_only_starts_once_the_room_is_listening_and_only_once()
    {
        var orchestrator = await StartedAsync();

        await orchestrator.BeginShowAsync(CancellationToken.None);
        await orchestrator.BeginShowAsync(CancellationToken.None);

        _clients.Latest.SentTexts.Should().ContainSingle().Which.Should().StartWith("SYSTEM:").And.Contain("starting");
    }

    [Fact]
    public async Task Speech_is_recorded_and_forwarded_and_a_long_silence_is_thinned_but_never_muted()
    {
        var orchestrator = await StartedAsync();

        for (var i = 0; i < 40; i++)
        {
            await orchestrator.OnAudioInAsync(Quiet(), CancellationToken.None);
        }

        var duringSilence = _clients.Latest.SentAudio.Count;
        duringSilence.Should().BeLessThan(40, "paying to stream silence to the model is the point of the gate");
        duringSilence.Should().BeGreaterThan(0, "the socket must never go completely quiet");

        await orchestrator.OnAudioInAsync(Loud(), CancellationToken.None);
        _clients.Latest.SentAudio.Count.Should().Be(duringSilence + 1, "the first loud frame always goes, so nobody is cut off mid-word");
        orchestrator.Session.Phase.Should().Be(SessionPhase.Intro);
    }

    [Fact]
    public async Task Every_frame_is_recorded_even_the_ones_that_are_not_worth_sending()
    {
        var orchestrator = await StartedAsync();

        for (var i = 0; i < 40; i++)
        {
            await orchestrator.OnAudioInAsync(Quiet(), CancellationToken.None);
        }

        await orchestrator.EndAsync("test", CancellationToken.None);

        // Stored as Opus, so the check is on what comes back out of it: forty frames of 400 samples each.
        var track = _blobs.Blobs[IAudioBlobStore.PlayersTrack(orchestrator.Session.MatchId, OpusAudio.Extension)];
        var pcm = OpusAudio.Decode(track, DebateOrchestrator.PlayerSampleRate);
        pcm.Length.Should().BeGreaterThanOrEqualTo(40 * 800,
            "the recording is what the analysis reads, so nothing may be thinned out of it");
        track.Length.Should().BeLessThan(pcm.Length, "and it costs less on disk than the samples it carries");
    }

    [Fact]
    public async Task A_malformed_frame_is_dropped_rather_than_forwarded()
    {
        var orchestrator = await StartedAsync();

        await orchestrator.OnAudioInAsync([], CancellationToken.None);
        await orchestrator.OnAudioInAsync(new byte[7], CancellationToken.None);
        await orchestrator.OnAudioInAsync(new byte[DebateOrchestrator.MaxFrameBytes + 2], CancellationToken.None);

        _clients.Latest.SentAudio.Should().BeEmpty();
    }

    [Fact]
    public async Task The_hosts_tool_calls_drive_the_rules_and_each_one_is_answered()
    {
        var orchestrator = await StartedAsync();

        await EmitAsync(
            Call(ToolDeclarations.SetPlayers, new { topic = "the thermostat", player1 = "Alex", player2 = "Sam" }),
            () => orchestrator.Session.Phase == SessionPhase.Setup);
        await EmitAsync(
            Call(ToolDeclarations.StartTurn, new { player = "player1" }),
            () => orchestrator.Session.Phase == SessionPhase.Debate);

        orchestrator.Session.Player1Name.Should().Be("AB", "the host does not get to rename anyone");
        orchestrator.Session.CurrentSpeaker.Should().Be(PlayerId.Player1);
        _clients.Latest.ToolResponses.Should().HaveCount(2).And.OnlyContain(r => r.Id == "fc-1");
        _sink.Latest!.Phase.Should().Be(SessionPhase.Debate, "the room is told as it happens");
    }

    [Fact]
    public async Task A_tool_call_naming_nobody_is_refused_without_moving_the_show_on()
    {
        var orchestrator = await StartedAsync();

        await EmitAsync(
            Call(ToolDeclarations.StartTurn, new { player = "the tall one" }),
            () => _clients.Latest.ToolResponses.Count == 1);

        orchestrator.Session.Phase.Should().Be(SessionPhase.Intro);
        _clients.Latest.ToolResponses[0].Result.ToString().Should().Contain("player1", "the host is told how to name somebody it can hand the floor to");
    }

    [Fact]
    public async Task The_ruling_survives_a_host_that_sends_its_reasons_as_one_string()
    {
        var orchestrator = await DebatingAsync();

        await EmitAsync(
            Call(ToolDeclarations.DeliverVerdict, new { winner_logic = "player1", winner_correct = "player2", overall = "player1", reasons = "clearer | had evidence | stayed on it" }),
            () => orchestrator.Session.Verdict is not null);

        orchestrator.Session.Verdict!.Reasons.Should().Equal(["clearer", "had evidence", "stayed on it"]);
        orchestrator.Session.Verdict.Overall.Should().Be(PlayerId.Player1);
    }

    [Fact]
    public async Task Somebody_who_holds_the_floor_too_long_is_interrupted_once_and_the_host_is_told_to_hand_over()
    {
        var orchestrator = await DebatingAsync();

        _clock.Advance(TimeSpan.FromSeconds(50));
        await orchestrator.OnAudioInAsync(Loud(), CancellationToken.None);
        await orchestrator.TickAsync(CancellationToken.None);

        _clients.Latest.SentTexts.Should().Contain(t => t.Contains("held the floor", StringComparison.Ordinal));
        orchestrator.Session.Turns.Should().Contain(t => t.Kind == TurnKind.Interrupt, "the interruption belongs in the transcript");
    }

    /// <summary>
    /// A sentence is transcribed in pieces, and the floor can change while it is still arriving — the long-talker
    /// nudge exists to make that happen. Whoever started the sentence said all of it.
    /// </summary>
    [Fact]
    public async Task A_sentence_keeps_the_name_of_whoever_started_it_even_if_the_floor_moves()
    {
        var orchestrator = await DebatingAsync("player1");

        await EmitAsync(new LiveServerEvent.InputTranscript("you never "), () => _sink.Captions.Count == 1);
        await EmitAsync(Call(ToolDeclarations.StartTurn, new { player = "player2" }), () => orchestrator.Session.CurrentSpeaker == PlayerId.Player2);
        // Each wait names the caption it is waiting for: "at least two captions" was already true, so the test
        // could read the sink before the work it was waiting on had happened.
        await EmitAsync(
            new LiveServerEvent.InputTranscript("listen"),
            () => _sink.Captions.Any(c => c.Final && c.Text.Contains("you never", StringComparison.Ordinal)));
        await EmitAsync(
            new LiveServerEvent.TurnComplete(),
            () => _sink.Captions.Any(c => c.Final && string.Equals(c.Text, "listen", StringComparison.Ordinal)));

        var theirs = _sink.Captions.Last(c => c.Final && c.Text.Contains("you never", StringComparison.Ordinal));
        theirs.Speaker.Should().Be(Speaker.Player1, "player one said those words, whoever holds the floor now");

        var next = _sink.Captions.Last(c => c.Final);
        next.Text.Should().Be("listen", "what the second one says is their own line, not the tail of somebody else's");
        next.Speaker.Should().Be(Speaker.Player2);
    }

    [Fact]
    public async Task What_the_room_hears_is_recorded_and_captions_are_kept_for_the_analysis()
    {
        var orchestrator = await DebatingAsync("player2");

        await EmitAsync(new LiveServerEvent.AudioOut(new byte[2_400]), () => _sink.Audio.Count == 1);
        await EmitAsync(new LiveServerEvent.InputTranscript("you never listen"), () => _sink.Captions.Count == 1);
        await EmitAsync(new LiveServerEvent.OutputTranscript("one at a time"), () => _sink.Captions.Count == 2);
        await EmitAsync(new LiveServerEvent.Interrupted(), () => _sink.Clears == 1);

        _sink.Captions[0].Speaker.Should().Be(Speaker.Player2, "the caption is labelled with whoever has the floor");
        _sink.Captions[1].Speaker.Should().Be(Speaker.Host);

        await orchestrator.EndAsync("test", CancellationToken.None);

        _blobs.Blobs.Should().ContainKey(IAudioBlobStore.HostTrack(orchestrator.Session.MatchId, OpusAudio.Extension));
        var transcript = JsonSerializer.Deserialize<TranscriptDto>(
            _blobs.Blobs[IAudioBlobStore.LiveTranscript(orchestrator.Session.MatchId)], JsonSerializerOptions.Web);
        transcript!.Text.Should().Contain("you never listen", "the show is transcribed once, not twice");
    }

    [Fact]
    public async Task Ending_stores_the_match_the_turns_and_the_recording_and_queues_the_analysis()
    {
        var orchestrator = await DebatingAsync();
        await orchestrator.OnAudioInAsync(Loud(), CancellationToken.None);

        await orchestrator.EndAsync("user pressed end", CancellationToken.None);

        var id = orchestrator.Session.MatchId;
        var stored = await _matches.GetAsync("user-1", id, CancellationToken.None);
        stored!.Status.Should().Be(SessionStatus.Analyzing);
        stored.EndedAt.Should().NotBeNull();
        stored.AudioBlobName.Should().Be(IAudioBlobStore.PlayersTrack(id, OpusAudio.Extension),
            "the name says which format it was stored in, which is how everything reading it back knows");
        (await _matches.GetTurnsAsync(id, CancellationToken.None)).Should().NotBeEmpty();
        (await _matches.GetAnalysisAsync(id, CancellationToken.None))!.Status.Should().Be(AnalysisStatus.Queued);
        _analysis.Queued.Should().ContainSingle().Which.Should().Be(("user-1", id));
        _sink.EndedMatch.Should().Be(id);
        orchestrator.Finished.Should().BeTrue();
        _clients.Latest.IsClosed.Should().BeTrue();
    }

    [Fact]
    public async Task Ending_twice_persists_once()
    {
        var orchestrator = await StartedAsync();
        await orchestrator.OnAudioInAsync(Loud(), CancellationToken.None);

        await orchestrator.EndAsync("first", CancellationToken.None);
        await orchestrator.EndAsync("second", CancellationToken.None);

        _analysis.Queued.Should().HaveCount(1);
    }

    [Fact]
    public async Task A_fight_with_no_recording_is_marked_failed_rather_than_queued_for_an_analysis_of_nothing()
    {
        var orchestrator = await StartedAsync();

        await orchestrator.EndAsync("nobody said anything", CancellationToken.None);

        var stored = await _matches.GetAsync("user-1", orchestrator.Session.MatchId, CancellationToken.None);
        stored!.Status.Should().Be(SessionStatus.Failed);
        _analysis.Queued.Should().BeEmpty();
    }

    [Fact]
    public async Task A_rehearsal_says_so_on_the_record()
    {
        var orchestrator = await StartedAsync(isFake: true);
        await orchestrator.OnAudioInAsync(Loud(), CancellationToken.None);

        await orchestrator.EndAsync("test", CancellationToken.None);

        var stored = await _matches.GetAsync("user-1", orchestrator.Session.MatchId, CancellationToken.None);
        stored!.IsFake.Should().BeTrue("a fight against a scripted host must never be mistaken for a real one");
    }

    [Fact]
    public async Task A_dropped_line_is_picked_up_again_and_the_host_is_told_to_carry_on()
    {
        var orchestrator = await StartedAsync();

        // The second client exists a moment before the orchestrator has finished telling it what happened, so the
        // wait is for the words, not for the connection: under a loaded suite the gap between the two is real.
        await EmitAsync(
            new LiveServerEvent.Closed("socket reset"),
            () => _clients.Created.Count == 2 && _clients.Latest.SentTexts.Any(t => t.Contains("line dropped", StringComparison.Ordinal)));

        _clients.Latest.Config!.ResumptionHandle.Should().BeNull("the fake never offered one, and one is not invented");
        orchestrator.Ended.Should().BeFalse();
    }

    [Fact]
    public async Task A_line_that_keeps_dropping_is_given_up_on_and_the_show_is_wrapped_up()
    {
        var orchestrator = await StartedAsync();

        for (var attempt = 0; attempt < 4; attempt++)
        {
            _clients.Latest.EmitClosed("socket reset");
            await WaitForAsync(() => _clients.Created.Count >= attempt + 2 || orchestrator.Ended);
        }

        await WaitForAsync(() => orchestrator.Ended);

        orchestrator.Ended.Should().BeTrue();
        _clients.Created.Should().HaveCount(4, "three reconnects, then it stops trying");
        _sink.Errors.Should().Contain(e => e.Contains("Lost the connection", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_room_that_everyone_left_is_ended_rather_than_left_running_at_our_expense()
    {
        var orchestrator = await StartedAsync();
        orchestrator.ClientDisconnected();

        _clock.Advance(TimeSpan.FromSeconds(_options.ClientGraceSeconds + 1));
        await orchestrator.TickAsync(CancellationToken.None);

        orchestrator.Ended.Should().BeTrue();
    }

    [Fact]
    public async Task A_show_that_runs_past_its_hard_cap_is_stopped()
    {
        var orchestrator = await StartedAsync();

        _clock.Advance(TimeSpan.FromSeconds(_options.MaxSessionSeconds + 1));
        await orchestrator.TickAsync(CancellationToken.None);

        orchestrator.Ended.Should().BeTrue();
    }

    [Fact]
    public async Task The_show_ends_when_the_ruling_has_actually_been_spoken_not_when_it_was_called()
    {
        var orchestrator = await DebatingAsync();
        await EmitAsync(
            Call(ToolDeclarations.DeliverVerdict, new { winner_logic = "player1", winner_correct = "player1", overall = "player1", reasons = new[] { "a", "b", "c" } }),
            () => orchestrator.Session.Phase == SessionPhase.Verdict);

        await EmitAsync(new LiveServerEvent.TurnComplete(), () => _sink.Snapshots.Count > 0);
        orchestrator.Ended.Should().BeFalse("the tool call and the spoken ruling are separate turns");

        await EmitAsync(new LiveServerEvent.AudioOut(new byte[2_400]), () => _sink.Audio.Count == 1);
        await EmitAsync(new LiveServerEvent.TurnComplete(), () => orchestrator.Ended);

        orchestrator.Ended.Should().BeTrue();
    }

    [Fact]
    public async Task A_host_that_never_stops_talking_after_the_ruling_is_stopped_by_the_clock()
    {
        var orchestrator = await DebatingAsync();
        await EmitAsync(
            Call(ToolDeclarations.DeliverVerdict, new { winner_logic = "player1", winner_correct = "player1", overall = "player1", reasons = new[] { "a", "b", "c" } }),
            () => orchestrator.Session.Phase == SessionPhase.Verdict);

        _clock.Advance(TimeSpan.FromSeconds(_options.VerdictGraceSeconds + 1));
        await orchestrator.TickAsync(CancellationToken.None);

        orchestrator.Ended.Should().BeTrue();
    }
}
