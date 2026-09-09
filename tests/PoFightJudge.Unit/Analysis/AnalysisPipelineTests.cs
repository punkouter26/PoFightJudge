using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using PoFightJudge.Api.Features.Ai;
using PoFightJudge.Api.Features.Ai.Fakes;
using PoFightJudge.Api.Features.Analysis;
using PoFightJudge.Api.Features.Diagnostics;
using PoFightJudge.Api.Features.Fight;
using PoFightJudge.Api.Features.Fighters;
using PoFightJudge.Api.Features.Profiles;
using PoFightJudge.Api.Features.Records;
using PoFightJudge.Api.Features.Storage;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;
using PoFightJudge.TestSupport;

namespace PoFightJudge.Unit.Analysis;

/// <summary>
/// What happens to a fight once it is over. Every step writes its status, so a failure is visible rather than a
/// fight that simply never becomes readable.
/// </summary>
public sealed class AnalysisPipelineTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 20, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly InMemoryAudioBlobStore _blobs = new();
    private readonly InMemoryWatchResultRepository _watchResults = new();
    private readonly InMemoryFighterResultRepository _fighterResults = new();
    private readonly InMemoryFighterRepository _fighters = new();
    private readonly InMemoryProfileRepository _profiles = new();
    private readonly InMemoryMatchRepository _matches;
    private readonly ServiceProvider _services;
    private readonly IGeminiJudgeClient _judge = Substitute.For<IGeminiJudgeClient>();
    private readonly AnalysisOptions _options = new() { UseLiveCaptionTranscript = true, MinCaptionTranscriptWords = 30 };

    public AnalysisPipelineTests()
    {
        _matches = new InMemoryMatchRepository(_watchResults, _fighterResults);
        var services = new ServiceCollection();
        services.AddSingleton<IMatchRepository>(_matches);
        services.AddSingleton<IFighterResultRepository>(_fighterResults);
        services.AddSingleton<IAudioBlobStore>(_blobs);
        services.AddSingleton<IFighterPersonaWriter>(new FighterPersonaWriter(
            _fighters, _fighterResults, _profiles,
            new FakeGeminiText(new AiLatencyTracker(), TimeSpan.Zero),
            GeminiModelOptions.Defaults,
            NullLogger<FighterPersonaWriter>.Instance));
        _services = services.BuildServiceProvider();

        _judge.JudgeAsync(Arg.Any<JudgeRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(Judged(call.Arg<JudgeRequest>())));
    }

    public void Dispose() => _services.Dispose();

    private AnalysisPipeline Pipeline(IGeminiTranscribeClient? transcriber = null, IGeminiFilesClient? files = null) => new(
        _services.GetRequiredService<IServiceScopeFactory>(),
        files ?? new FakeAnalysisClients.Files(),
        new GeminiRecordingTranscriber(transcriber ?? new FakeAnalysisClients.Transcriber()),
        _judge,
        _clock,
        Options.Create(_options),
        NullLogger<AnalysisPipeline>.Instance);

    /// <summary>A finished fight with a recording, ready to be read.</summary>
    private async Task<MatchDto> RecordedAsync(string one = "AL", string two = "SM", bool withAudio = true)
    {
        // Both fighters exist from the moment a fight starts; the persona written afterwards is keyed on them.
        await _fighters.EnsureAsync(FighterId.From(one), Now, ProfileRole.Husband);
        await _fighters.EnsureAsync(FighterId.From(two), Now, ProfileRole.Wife);
        var match = new MatchDto(
            MatchId.New(), "user-1", MatchMode.Fight, Now.AddMinutes(-5), Now,
            "the thermostat", MatchSide.Human(one), MatchSide.Human(two),
            SessionPhase.Done, SessionStatus.Analyzing, string.Empty, "The host called it for AL.", IsFake: false)
        {
            // Stored the way the orchestrator stores one: Opus on disk, whatever the model is later sent.
            AudioBlobName = withAudio ? IAudioBlobStore.PlayersTrack(MatchId.New(), OpusAudio.Extension) : null,
        };

        if (withAudio)
        {
            using var recording = new MemoryStream(OpusAudio.Encode(new byte[16_000 * 2], 16_000));
            await _blobs.UploadAsync(match.AudioBlobName!, recording, OpusAudio.ContentType, CancellationToken.None);
        }

        await _matches.UpsertAsync(match, CancellationToken.None);
        await _matches.SaveTurnsAsync(match.Id,
        [
            new TurnDto(match.Id, 0, Speaker.Player1, TurnKind.Talk, string.Empty) { StartSeconds = 0, EndSeconds = 9 },
            new TurnDto(match.Id, 1, Speaker.Player2, TurnKind.Talk, string.Empty) { StartSeconds = 9, EndSeconds = 19 },
        ], CancellationToken.None);
        return match;
    }

    private static JudgeOutputDto Judged(JudgeRequest request) => new(
        Assessment("clear", "you always say that"),
        Assessment("loud", "that is nonsense"),
        new JudgeOverallDto("player1", "player2", "player1", ["clearer", "kinder", "right"], "One of them listened."));

    private static PlayerAssessmentDto Assessment(string tone, string quote) => new(
        "B2", "clear enough", 1, ["a slip"], 6, 7, 8, [new FallacyDto("Straw man", quote)], 5, 6, 7, 80, [],
        new EmotionProfileDto(10, 40, 30, 5, 0, 15), [], quote, [tone, "dry", "fast"],
        "steady", "even", "varied", 7, 6, 4, 5, quote, "worst", ["one", "two", "three"]);

    private async Task<AnalysisRecordDto?> AnalysisOf(MatchId id) => await _matches.GetAnalysisAsync(id, CancellationToken.None);

    [Fact]
    public async Task A_finished_fight_comes_out_readable_with_both_fighters_on_the_record()
    {
        var match = await RecordedAsync();

        using var pipeline = Pipeline();
        await pipeline.ProcessAsync("user-1", match.Id, CancellationToken.None);

        var analysis = await AnalysisOf(match.Id);
        analysis!.Status.Should().Be(AnalysisStatus.Ready);
        analysis.ReportJson.Should().NotBeNullOrWhiteSpace();

        var stored = await _matches.GetAsync("user-1", match.Id, CancellationToken.None);
        stored!.Status.Should().Be(SessionStatus.Ready);
        stored.Winner.Should().Be("AL", "the ruling names a tag, which is what a record is keyed on");

        var rows = await _fighterResults.ListForAsync(FighterId.From("AL"), "user-1", CancellationToken.None);
        rows.Should().ContainSingle().Which.Won.Should().BeTrue();
        (await _fighterResults.ListForAsync(FighterId.From("SM"), "user-1", CancellationToken.None)).Should().ContainSingle()
            .Which.Won.Should().BeFalse();
    }

    [Fact]
    public async Task Both_fighters_come_out_of_it_with_a_persona_the_other_channels_can_seat()
    {
        var match = await RecordedAsync();

        using var pipeline = Pipeline();
        await pipeline.ProcessAsync("user-1", match.Id, CancellationToken.None);

        var al = await _profiles.GetByIdAsync(ProfileId.From("AL"));
        al.Should().NotBeNull("what they said in 2P is now somebody CPU and 1P can put in a seat");
        al!.FromFights.Should().BeTrue();
        al.Role.Should().Be(ProfileRole.Husband);
        (await _profiles.GetByIdAsync(ProfileId.From("SM")))!.Role.Should().Be(ProfileRole.Wife);
    }

    /// <summary>
    /// Recordings are stored in Opus. The Files API takes one and reports it ACTIVE, and then generateContent
    /// answers 400 for it — measured against the live endpoint on 2026-09-07 — so what goes up is PCM.
    /// </summary>
    [Fact]
    public async Task The_model_is_sent_samples_however_the_recording_was_stored()
    {
        var match = await RecordedAsync();
        var files = new RecordingFiles();

        using var pipeline = Pipeline(files: files);
        await pipeline.ProcessAsync("user-1", match.Id, CancellationToken.None);

        var upload = files.Uploads.Should().ContainSingle().Subject;
        upload.MimeType.Should().Be("audio/wav");
        upload.Name.Should().EndWith(".wav");
        upload.Bytes.Should().StartWith("RIFF"u8.ToArray(), "what goes up is a WAV, whatever is on disk");
    }

    /// <summary>A files client that keeps what it was handed, so a test can say what the model was actually sent.</summary>
    private sealed class RecordingFiles : IGeminiFilesClient
    {
        public List<(string Name, string MimeType, byte[] Bytes)> Uploads { get; } = [];

        public async Task<GeminiFile> UploadAsync(Stream content, long length, string mimeType, string displayName, CancellationToken ct)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, ct);
            Uploads.Add((displayName, mimeType, copy.ToArray()));
            return new GeminiFile($"files/{displayName}", $"https://fake.invalid/{displayName}", "ACTIVE", mimeType);
        }

        public Task<GeminiFile> GetAsync(string name, CancellationToken ct) =>
            Task.FromResult(new GeminiFile(name, $"https://fake.invalid/{name}", "ACTIVE", "audio/wav"));
    }

    [Fact]
    public async Task Each_fighter_keeps_what_this_fight_said_about_how_they_argue()
    {
        var match = await RecordedAsync();

        using var pipeline = Pipeline();
        await pipeline.ProcessAsync("user-1", match.Id, CancellationToken.None);

        var style = (await _fighterResults.ListForAsync(FighterId.From("AL"), "user-1", CancellationToken.None))[0].Style;
        style.Tone.Should().Contain("clear");
        style.Cefr.Should().Be("B2");
        style.Fallacies.Should().Contain("Straw man");
        style.Emotions.Should().StartWith("confident", "it is the strongest one that is worth remembering");
        style.BestQuote.Should().NotBeNullOrWhiteSpace();
        style.Opener.Should().NotBeNullOrWhiteSpace("how somebody opens is the most repeatable thing about them");
        style.Tips.Should().HaveCount(3);
    }

    [Fact]
    public async Task A_fight_with_almost_nothing_in_it_is_not_handed_to_the_judge_to_invent_one()
    {
        var thin = Substitute.For<IGeminiTranscribeClient>();
        thin.TranscribeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new TranscriptDto("well", [new TranscriptWord("well", "spk_1", 0, 0.4)])));
        var match = await RecordedAsync();

        using var pipeline = Pipeline(thin);
        await pipeline.ProcessAsync("user-1", match.Id, CancellationToken.None);

        await _judge.DidNotReceive().JudgeAsync(Arg.Any<JudgeRequest>(), Arg.Any<CancellationToken>());
        var analysis = await AnalysisOf(match.Id);
        analysis!.Status.Should().Be(AnalysisStatus.Ready, "a fight that produced nothing still has a report saying so");
        analysis.ReportJson.Should().Contain("not enough", "the headline must not contradict the recording");
    }

    [Fact]
    public async Task The_transcript_the_fight_already_paid_for_is_used_rather_than_bought_again()
    {
        var transcriber = Substitute.For<IGeminiTranscribeClient>();
        var match = await RecordedAsync();
        await StoreCaptionsAsync(match.Id, 40);

        using var pipeline = Pipeline(transcriber);
        await pipeline.ProcessAsync("user-1", match.Id, CancellationToken.None);

        await transcriber.DidNotReceive().TranscribeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        (await AnalysisOf(match.Id))!.Status.Should().Be(AnalysisStatus.Ready);
    }

    [Fact]
    public async Task Captions_too_thin_to_judge_on_are_worth_paying_to_replace()
    {
        var transcriber = Substitute.For<IGeminiTranscribeClient>();
        var rehearsal = await new FakeAnalysisClients.Transcriber().TranscribeAsync(string.Empty, string.Empty, CancellationToken.None);
        transcriber.TranscribeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(rehearsal));
        var match = await RecordedAsync();
        await StoreCaptionsAsync(match.Id, 5);

        using var pipeline = Pipeline(transcriber);
        await pipeline.ProcessAsync("user-1", match.Id, CancellationToken.None);

        await transcriber.Received(1).TranscribeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_fight_that_fails_says_so_rather_than_staying_unread_forever()
    {
        _judge.JudgeAsync(Arg.Any<JudgeRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<JudgeOutputDto>(new InvalidOperationException("the judge is out")));
        var match = await RecordedAsync();
        using var pipeline = Pipeline();

        await pipeline.SubmitAsync("user-1", match.Id, CancellationToken.None);
        await pipeline.StartAsync(CancellationToken.None);
        await WaitForAsync(async () => (await AnalysisOf(match.Id))?.Status == AnalysisStatus.Failed);
        await pipeline.StopAsync(CancellationToken.None);

        var analysis = await AnalysisOf(match.Id);
        analysis!.Status.Should().Be(AnalysisStatus.Failed);
        analysis.Error.Should().Be(AnalysisPipeline.UserFacingFailure, "the detail belongs in the log, not in front of the room");
        (await _matches.GetAsync("user-1", match.Id, CancellationToken.None))!.Status.Should().Be(SessionStatus.Failed);
    }

    [Fact]
    public async Task A_fight_deleted_while_it_was_being_read_does_not_come_back_as_a_row()
    {
        _judge.JudgeAsync(Arg.Any<JudgeRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<JudgeOutputDto>(new InvalidOperationException("the judge is out")));
        var match = await RecordedAsync();
        await _matches.DeleteAsync("user-1", match.Id, CancellationToken.None);
        using var pipeline = Pipeline();

        await pipeline.SubmitAsync("user-1", match.Id, CancellationToken.None);
        await pipeline.StartAsync(CancellationToken.None);
        await WaitForAsync(() => Task.FromResult(true));
        await pipeline.StopAsync(CancellationToken.None);

        (await AnalysisOf(match.Id)).Should().BeNull("somebody deleted it, and it must stay deleted");
    }

    [Fact]
    public async Task A_fight_with_no_recording_fails_loudly_rather_than_reading_silence()
    {
        var match = await RecordedAsync(withAudio: false);

        using var pipeline = Pipeline();
        var process = async () => await pipeline.ProcessAsync("user-1", match.Id, CancellationToken.None);

        await process.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no recording*");
    }

    [Fact]
    public async Task Somebody_elses_fight_is_not_readable()
    {
        var match = await RecordedAsync();

        using var pipeline = Pipeline();
        var process = async () => await pipeline.ProcessAsync("user-2", match.Id, CancellationToken.None);

        await process.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not found*");
    }

    private async Task StoreCaptionsAsync(MatchId id, int words)
    {
        var transcript = new TranscriptDto(
            string.Join(' ', Enumerable.Range(0, words).Select(i => $"word{i}")),
            [.. Enumerable.Range(0, words).Select(i => new TranscriptWord($"word{i}", i % 2 == 0 ? "live_1" : "live_2", i * 0.5, (i * 0.5) + 0.4))]);
        using var json = new MemoryStream(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(transcript, System.Text.Json.JsonSerializerOptions.Web));
        await _blobs.UploadAsync(IAudioBlobStore.LiveTranscript(id), json, "application/json", CancellationToken.None);
    }

    /// <summary>The background loop runs on its own thread, so a test waits for it rather than assuming.</summary>
    private static async Task WaitForAsync(Func<Task<bool>> until)
    {
        for (var attempt = 0; attempt < 400 && !await until(); attempt++)
        {
            await Task.Delay(5, CancellationToken.None);
        }
    }
}
