using System.Diagnostics.CodeAnalysis;
using PoMarriedFight.Api.Common;
using PoMarriedFight.Shared.Identifiers;
using PoMarriedFight.Shared.Models;

namespace PoMarriedFight.Api.Features.Fight;

[SuppressMessage(
    "Design",
    "CA1008:Enums should have zero value",
    Justification = "There is no third person in the room, so a zero member would name a player who does not exist. " +
        "TryParse reports a value it could not read through its return value rather than by handing back a default.")]
public enum PlayerId
{
    Player1 = 1,
    Player2 = 2,
}

public enum NudgeKind
{
    /// <summary>Somebody has held the floor past the long-talker limit.</summary>
    LongTalker,

    /// <summary>Nobody has spoken for the silence threshold; the host should say something.</summary>
    Silence,

    /// <summary>A second silence window elapsed; the host should move on to the next step.</summary>
    SilenceAdvance,

    /// <summary>The questioning window is open; the host may cut in.</summary>
    ProbeWindowOpen,

    /// <summary>The argument has run its full time and must stop now.</summary>
    DebateCap,

    /// <summary>The questioning ran long; the host must rule now.</summary>
    ProbeCap,
}

/// <summary>A deterministic instruction for the host, produced by the state machine's clock.</summary>
public sealed record DebateNudge(NudgeKind Kind, string Message);

/// <summary>The ruling as the host called it, before it is turned into a stored result.</summary>
public sealed record VerdictCall(PlayerId WinnerLogic, PlayerId WinnerCorrect, PlayerId Overall, IReadOnlyList<string> Reasons);

public static class PlayerIdExtensions
{
    public static Speaker ToSpeaker(this PlayerId id) => id == PlayerId.Player1 ? Speaker.Player1 : Speaker.Player2;

    public static PlayerId Other(this PlayerId id) => id == PlayerId.Player1 ? PlayerId.Player2 : PlayerId.Player1;

    /// <summary>The other side. The host has no opponent and maps to Player1 for callers that need a value.</summary>
    public static Speaker Other(this Speaker speaker) => speaker == Speaker.Player1 ? Speaker.Player2 : Speaker.Player1;

    /// <summary>Reads the values a model might use for a player: "player1", "Player 2", "p1", "two".</summary>
    public static bool TryParse(string? value, out PlayerId id)
    {
        var cleaned = (value ?? string.Empty).Trim().ToLowerInvariant().Replace(" ", string.Empty, StringComparison.Ordinal);
        switch (cleaned)
        {
            case "player1" or "p1" or "1" or "one":
                id = PlayerId.Player1;
                return true;
            case "player2" or "p2" or "2" or "two":
                id = PlayerId.Player2;
                return true;
            default:
                id = default;
                return false;
        }
    }
}

/// <summary>
/// The rules of one fight: Intro → Setup → Debate → Probe → Verdict → Done. Callers feed it tool calls and speech
/// activity with explicit timestamps and <see cref="Tick"/> hands back nudges. No I/O and no timers of its own, so
/// a whole show is reproducible from a list of moments.
/// </summary>
public sealed class DebateSession(DebateOptions options, MatchId matchId, DateTimeOffset startedAt, ShowSetup? setup = null)
{
    public const int MaxShortText = 200;
    public const int MaxQuestionText = 1_000;
    public const int MaxVerdictText = 8_000;

    private readonly List<TurnDto> _turns = [];
    private readonly HashSet<NudgeKind> _nudged = [];
    private DateTimeOffset _lastSpeech = startedAt;
    private DateTimeOffset _turnStartedAt = startedAt;
    private DateTimeOffset? _debateStartedAt;
    private DateTimeOffset? _probeStartedAt;
    private int? _openTurnIndex;
    private int _silenceLevel;

    public MatchId MatchId { get; } = matchId;

    public DateTimeOffset StartedAt { get; } = startedAt;

    public SessionPhase Phase { get; private set; } = SessionPhase.Intro;

    public string Topic { get; private set; } = setup?.Topic ?? string.Empty;

    public string Player1Name { get; private set; } = setup?.HasPlayers == true ? setup.Player1 : "Player 1";

    public string Player2Name { get; private set; } = setup?.HasPlayers == true ? setup.Player2 : "Player 2";

    /// <summary>Who is hosting. Carried on every snapshot so the room can theme itself.</summary>
    public HostPersonaId Persona { get; } = setup?.Persona ?? HostPersonaId.Referee;

    public PlayerId? CurrentSpeaker { get; private set; }

    public VerdictCall? Verdict { get; private set; }

    public DateTimeOffset? VerdictAt { get; private set; }

    public IReadOnlyList<TurnDto> Turns => _turns;

    public TimeSpan DebateElapsed(DateTimeOffset now) => _debateStartedAt is null ? TimeSpan.Zero : now - _debateStartedAt.Value;

    public TimeSpan DebateRemaining(DateTimeOffset now)
    {
        var remaining = TimeSpan.FromSeconds(options.MaxDebateSeconds) - DebateElapsed(now);
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }

    public string NameOf(PlayerId id) => id == PlayerId.Player1 ? Player1Name : Player2Name;

    public Outcome SetPlayers(string topic, string player1, string player2, DateTimeOffset now)
    {
        if (Phase is not (SessionPhase.Intro or SessionPhase.Setup))
        {
            return Outcome.Fail($"Cannot set players during {Phase}.");
        }

        // The tags were entered before the show and key the fighter tables, so the host never gets to rename anyone,
        // and a topic they agreed before the microphone went on is theirs rather than the host's to rewrite. The host
        // is told to restate an agreed topic, and a restatement that came back slightly different would otherwise
        // replace what they actually typed.
        if (setup?.HasTopic != true)
        {
            Topic = Clean(topic, MaxShortText, Topic.Length > 0 ? Topic : "an open topic");
        }

        if (setup?.HasPlayers != true)
        {
            Player1Name = Clean(player1, MaxShortText, "Player 1");
            Player2Name = Clean(player2, MaxShortText, "Player 2");
        }

        Phase = SessionPhase.Setup;
        NoteSpeech(now);
        return Outcome.Ok;
    }

    public Outcome StartTurn(PlayerId player, DateTimeOffset now)
    {
        if (Phase is SessionPhase.Setup)
        {
            Phase = SessionPhase.Debate;
            _debateStartedAt = now;
        }

        if (Phase is not SessionPhase.Debate)
        {
            return Outcome.Fail($"Cannot start a turn during {Phase}.");
        }

        OpenTurn(player, TurnKind.Talk, string.Empty, now);
        _nudged.Remove(NudgeKind.LongTalker);
        return Outcome.Ok;
    }

    public Outcome EndDebate(DateTimeOffset now)
    {
        if (Phase is not SessionPhase.Debate)
        {
            return Outcome.Fail($"Cannot end the argument during {Phase}.");
        }

        CloseOpenTurn(now);
        CurrentSpeaker = null;
        Phase = SessionPhase.Probe;
        _probeStartedAt = now;
        NoteSpeech(now);
        return Outcome.Ok;
    }

    public Outcome AskProbe(PlayerId player, string question, DateTimeOffset now)
    {
        // A host that skips straight to questions must not be left stuck in the wrong phase.
        if (Phase is SessionPhase.Debate)
        {
            EndDebate(now);
        }

        if (Phase is not SessionPhase.Probe)
        {
            return Outcome.Fail($"Cannot ask a question during {Phase}.");
        }

        OpenTurn(player, TurnKind.Probe, Clean(question, MaxQuestionText, string.Empty), now);
        return Outcome.Ok;
    }

    public Outcome DeliverVerdict(VerdictCall verdict, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(verdict);
        if (Phase is SessionPhase.Verdict or SessionPhase.Done)
        {
            return Outcome.Fail("The ruling has already been given.");
        }

        CloseOpenTurn(now);
        CurrentSpeaker = null;
        Verdict = verdict;
        VerdictAt ??= now;
        Phase = SessionPhase.Verdict;
        _turns.Add(new TurnDto(MatchId, _turns.Count, Speaker.Host, TurnKind.Verdict, Clean(string.Join(" | ", verdict.Reasons), MaxVerdictText, string.Empty))
        {
            StartSeconds = Seconds(now),
        });
        return Outcome.Ok;
    }

    /// <summary>Called once the host has finished speaking the ruling, or the show is cut short.</summary>
    public void Complete(DateTimeOffset now)
    {
        CloseOpenTurn(now);
        CurrentSpeaker = null;
        Phase = SessionPhase.Done;
    }

    /// <summary>Any audio activity resets the silence clock. The long-talker clock is per turn and is not affected.</summary>
    public void NoteSpeech(DateTimeOffset now)
    {
        _lastSpeech = now;
        _silenceLevel = 0;
        _nudged.Remove(NudgeKind.Silence);
        _nudged.Remove(NudgeKind.SilenceAdvance);
    }

    /// <summary>Records a host interruption. The turn is attributed to whoever was cut off.</summary>
    public void NoteInterrupt(DateTimeOffset now)
    {
        if (CurrentSpeaker is { } speaker)
        {
            _turns.Add(new TurnDto(MatchId, _turns.Count, speaker.ToSpeaker(), TurnKind.Interrupt, $"Host interrupted {NameOf(speaker)}")
            {
                StartSeconds = Seconds(now),
                EndSeconds = Seconds(now),
            });
        }

        _nudged.Add(NudgeKind.LongTalker);
    }

    /// <summary>Reads the clocks. Each nudge fires once per condition, and only fires again once that condition has reset.</summary>
    public IReadOnlyList<DebateNudge> Tick(DateTimeOffset now)
    {
        var nudges = new List<DebateNudge>();
        if (Phase is SessionPhase.Done or SessionPhase.Verdict)
        {
            return nudges;
        }

        var silence = now - _lastSpeech;
        if (_silenceLevel == 0 && silence >= TimeSpan.FromSeconds(options.SilenceSeconds) && !_nudged.Contains(NudgeKind.Silence))
        {
            _silenceLevel = 1;
            _nudged.Add(NudgeKind.Silence);
            nudges.Add(new DebateNudge(NudgeKind.Silence, SilenceMessage()));
        }
        else if (_silenceLevel == 1 && silence >= TimeSpan.FromSeconds(options.SilenceSeconds * 2) && !_nudged.Contains(NudgeKind.SilenceAdvance))
        {
            _silenceLevel = 2;
            _nudged.Add(NudgeKind.SilenceAdvance);
            nudges.Add(new DebateNudge(NudgeKind.SilenceAdvance, "SYSTEM: Still silence. Move on to the next step now without waiting."));
        }

        if (Phase is SessionPhase.Debate)
        {
            AddDebateNudges(now, nudges);
        }
        else if (Phase is SessionPhase.Probe && _probeStartedAt is { } probeStart
            && !_nudged.Contains(NudgeKind.ProbeCap) && now - probeStart >= TimeSpan.FromSeconds(options.MaxProbeSeconds))
        {
            _nudged.Add(NudgeKind.ProbeCap);
            nudges.Add(new DebateNudge(NudgeKind.ProbeCap, "SYSTEM: Question time is over. Give the ruling now (call deliver_verdict, then announce it)."));
        }

        return nudges;
    }

    public DebateSnapshotDto Snapshot(DateTimeOffset now) => new(
        MatchId,
        Phase,
        Topic,
        Player1Name,
        Player2Name,
        CurrentSpeaker?.ToSpeaker(),
        (int)DebateElapsed(now).TotalSeconds,
        (int)DebateRemaining(now).TotalSeconds,
        Verdict is null ? null : new VerdictDto(Verdict.WinnerLogic.ToSpeaker(), Verdict.WinnerCorrect.ToSpeaker(), Verdict.Overall.ToSpeaker(), Verdict.Reasons),
        Persona,
        (int)(now - StartedAt).TotalSeconds);

    /// <summary>Trims model-supplied text and caps its length, so a stored property stays inside what Table Storage takes.</summary>
    public static string Clean(string? value, int max, string fallback)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return fallback;
        }

        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    private void AddDebateNudges(DateTimeOffset now, List<DebateNudge> nudges)
    {
        var elapsed = DebateElapsed(now);

        if (CurrentSpeaker is { } speaker && !_nudged.Contains(NudgeKind.LongTalker)
            && now - _turnStartedAt >= TimeSpan.FromSeconds(options.LongTalkerSeconds))
        {
            _nudged.Add(NudgeKind.LongTalker);
            nudges.Add(new DebateNudge(
                NudgeKind.LongTalker,
                $"SYSTEM: {NameOf(speaker)} has held the floor for {options.LongTalkerSeconds} seconds. Interrupt politely and hand over to {NameOf(speaker.Other())} (call start_turn)."));
        }

        if (!_nudged.Contains(NudgeKind.ProbeWindowOpen) && elapsed >= TimeSpan.FromSeconds(options.ProbeWindowStartSeconds))
        {
            _nudged.Add(NudgeKind.ProbeWindowOpen);
            nudges.Add(new DebateNudge(
                NudgeKind.ProbeWindowOpen,
                $"SYSTEM: {options.ProbeWindowStartSeconds} seconds of arguing done. When you have heard enough (before {options.ProbeWindowEndSeconds} seconds), call end_debate and start questioning them one at a time with ask_probe."));
        }

        if (!_nudged.Contains(NudgeKind.DebateCap) && elapsed >= TimeSpan.FromSeconds(options.MaxDebateSeconds))
        {
            _nudged.Add(NudgeKind.DebateCap);
            nudges.Add(new DebateNudge(NudgeKind.DebateCap, "SYSTEM: Time is up. Stop the argument immediately (call end_debate) and begin the questions."));
        }
    }

    private string SilenceMessage() => Phase switch
    {
        SessionPhase.Intro => "SYSTEM: Nobody has answered. Ask again for the topic and both names, briefly.",
        SessionPhase.Setup => $"SYSTEM: Silence. Kick things off — invite {Player1Name} to open (call start_turn).",
        SessionPhase.Debate when CurrentSpeaker is { } speaker => $"SYSTEM: {NameOf(speaker)} has gone quiet. Prompt them by name, or hand over to {NameOf(speaker.Other())}.",
        SessionPhase.Probe => "SYSTEM: Silence. Repeat or simplify your question, or move to the other one.",
        _ => "SYSTEM: Silence. Keep the show moving.",
    };

    private void OpenTurn(PlayerId player, TurnKind kind, string text, DateTimeOffset now)
    {
        CloseOpenTurn(now);
        CurrentSpeaker = player;
        _turnStartedAt = now;
        NoteSpeech(now);
        _openTurnIndex = _turns.Count;
        _turns.Add(new TurnDto(MatchId, _turns.Count, player.ToSpeaker(), kind, text) { StartSeconds = Seconds(now) });
    }

    /// <summary>Closes whichever turn is open, wherever it sits in the list — interrupt entries may have been added after it.</summary>
    private void CloseOpenTurn(DateTimeOffset now)
    {
        if (_openTurnIndex is { } index)
        {
            _turns[index] = _turns[index] with { EndSeconds = Seconds(now) };
            _openTurnIndex = null;
        }
    }

    private double Seconds(DateTimeOffset now) => Math.Round((now - StartedAt).TotalSeconds, 2);
}
