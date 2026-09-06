using PoMarriedFight.Shared.Identifiers;

namespace PoMarriedFight.Shared.Models;

/// <summary>
/// One heckle the spectator can throw into a live argument. The client renders buttons and the server builds its
/// prompt directive from this same list, so a new interjection is one entry here rather than an edit on both sides.
/// </summary>
/// <param name="Key">Wire value sent on the generate-round request.</param>
/// <param name="Icon">Emoji shown on the button.</param>
/// <param name="Label">Short button caption.</param>
/// <param name="Tooltip">Plain-English description of the effect.</param>
/// <param name="PromptDirective">Line injected into the prompt's current-state block.</param>
public sealed record Interjection(string Key, string Icon, string Label, string Tooltip, string PromptDirective);

/// <summary>The interjection catalogue, shared by the play page and the prompt builder so the two can never drift.</summary>
public static class Interjections
{
    /// <summary>Key of the interrupting slap — the one interjection with special turn handling.</summary>
    public const string SlapKey = "slap";

    public static IReadOnlyList<Interjection> All { get; } =
    [
        new(
            Key: SlapKey,
            Icon: "👋",
            Label: "SLAP",
            Tooltip: "Interrupt the speaker mid-sentence — they react furiously and the opponent's turn is skipped. Once per match.",
            PromptDirective: "⚠ They were just SLAPPED across the face and are FURIOUS — open with outrage at being struck, then escalate hard."),
    ];

    /// <summary>Resolves a wire key to its definition; null for an unknown or absent key, so an old client simply gets no directive.</summary>
    public static Interjection? Find(string? key) =>
        string.IsNullOrWhiteSpace(key) ? null : All.FirstOrDefault(i => string.Equals(i.Key, key, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Argument analytics for one speaker, computed from what they actually said. Every value is 0–100 except the raw fallacy count.</summary>
public sealed record AdvancedStatsDto(
    double PassiveAggressionIndex,
    double HistoricalGrievanceRate,
    double BlameMetric,
    int LogicalFallacyCount,
    double VolumeScore,
    double WordCountDominance,
    double EmotionalVolatility,
    double DeflectionCoefficient,
    double LexicalComplexity,
    double ApologyToInsultRatio)
{
    public static AdvancedStatsDto Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}

/// <summary>One line of a WATCH match as the client holds it. Audio travels separately so a round can render before it can speak.</summary>
public sealed record WatchRoundDto(string Speaker, string Text, string Mood)
{
    /// <summary>Set once the round's audio has been fetched; the format is whatever the provider chain actually returned.</summary>
    public TtsAudioDto? Audio { get; init; }
}

/// <summary>
/// Asks for the next line. The client drives the loop, so it sends the whole state rather than the server holding a
/// session. Each side is a persona or a live person; the server only ever generates a line for a persona, because a
/// person speaks for themselves.
/// </summary>
public sealed record GenerateRoundRequest(
    MatchSide Husband,
    MatchSide Wife,
    IReadOnlyList<WatchRoundDto> History,
    string Speaker,
    string? Topic = null,
    string? Interjection = null,
    bool WasSlapped = false);

/// <summary>One generated line plus the attitude it was asked for, so the UI can show why it landed the way it did.</summary>
public sealed record GenerateRoundResponse(string Text, string Mood, string Attitude, bool IsFake);

/// <summary>Asks for the audio of a line that has already been generated.</summary>
public sealed record RoundAudioRequest(ProfileId Speaker, string Text);

/// <summary>
/// Ends a match: the judge rules on the transcript and the whole thing is persisted. A persona side gets a watch
/// result; a person's side gets a fighter result, so their profile grows from this debate too.
/// <paramref name="MatchId"/> is only for re-judging a match the caller already owns.
/// </summary>
public sealed record VerdictRequest(MatchSide Husband, MatchSide Wife, IReadOnlyList<WatchRoundDto> Rounds, string? Topic = null, MatchId? MatchId = null);

/// <summary>The ruling. <paramref name="Winner"/> is the winning side's id, or empty when the judge called neither.</summary>
public sealed record VerdictResponse(
    MatchId MatchId,
    string Winner,
    string Verdict,
    int HusbandScore,
    int WifeScore,
    AdvancedStatsDto HusbandStats,
    AdvancedStatsDto WifeStats,
    bool Persisted);

/// <summary>A recorded SELF turn on its way to the transcriber. The WAV is base64 because it rides in a JSON body.</summary>
public sealed record TranscribeRequest(string WavBase64);

/// <summary>What was heard. An empty <paramref name="Text"/> means nothing intelligible was said — a legitimate outcome, not an error.</summary>
public sealed record TranscribeResponse(string Text, bool IsFake);
