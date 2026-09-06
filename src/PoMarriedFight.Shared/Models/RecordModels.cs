using PoMarriedFight.Shared.Identifiers;

namespace PoMarriedFight.Shared.Models;

/// <summary>
/// One match, either mode. The two engines meet here: a WATCH match names two personas, a FIGHT names two fighter
/// tags, and everything else — owner, topic, timing, outcome, whether the AI was faked — is shared, so history and
/// deletion are one query and one cascade rather than two of each.
/// </summary>
public sealed record MatchDto(
    MatchId Id,
    string UserId,
    MatchMode Mode,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string Topic,
    string Side1,
    string Side2,
    string Side1Name,
    string Side2Name,
    SessionPhase Phase,
    SessionStatus Status,
    string Winner,
    string? Verdict,
    bool IsFake)
{
    /// <summary>Host persona for a fight; empty for a watch.</summary>
    public string Persona { get; init; } = string.Empty;

    /// <summary>Name of the recorded audio for a fight; a watch stores audio per round instead.</summary>
    public string? AudioBlobName { get; init; }

    /// <summary>True when the judge called neither side.</summary>
    public bool IsDraw => string.IsNullOrEmpty(Winner);
}

/// <summary>
/// One turn of a match. A WATCH line is <see cref="TurnKind.Round"/> and carries a mood; a live fight's turns carry
/// timings from the recording. <paramref name="Index"/> is the play order.
/// </summary>
public sealed record TurnDto(MatchId MatchId, int Index, Speaker Speaker, TurnKind Kind, string Text)
{
    public double StartSeconds { get; init; }

    public double? EndSeconds { get; init; }

    /// <summary>Register of a WATCH line; empty for a live turn.</summary>
    public string Mood { get; init; } = string.Empty;

    public string? AudioBlobName { get; init; }

    /// <summary>Wire format of the stored audio: <c>pcm</c> or <c>mp3</c>.</summary>
    public string AudioFormat { get; init; } = "pcm";
}

/// <summary>The post-fight analysis, or its progress. The report itself is JSON so the pipeline can grow without a storage migration.</summary>
public sealed record AnalysisRecordDto(MatchId MatchId, AnalysisStatus Status, string? ReportJson, string? Error, DateTimeOffset UpdatedAt);

/// <summary>
/// One persona's record of one finished WATCH match. Written per side, so a profile's whole record is a
/// single-partition read and re-judging a match overwrites rather than double-counting.
/// </summary>
public sealed record WatchResultDto(
    string Initials,
    MatchId MatchId,
    DateTimeOffset At,
    string Topic,
    string Opponent,
    bool Won,
    bool Draw,
    int Score,
    AdvancedStatsDto Stats);

/// <summary>
/// How a fighter argued in one fight, kept alongside the result so the style profile never has to re-read a report.
/// Everything here is derived from the analysis; nothing a fighter types goes in it.
/// </summary>
public sealed record FightStyleSnapshot(
    string Tone,
    IReadOnlyList<string> Phrases,
    IReadOnlyList<string> Fallacies,
    string Opener,
    string Cefr,
    IReadOnlyList<string> Emotions,
    string BestQuote,
    IReadOnlyList<string> Tips)
{
    public static FightStyleSnapshot Empty { get; } = new(string.Empty, [], [], string.Empty, string.Empty, [], string.Empty, []);
}

/// <summary>One fighter's record of one finished fight, with the style snapshot the host digest is built from.</summary>
public sealed record FightResultDto(
    string Tag,
    MatchId MatchId,
    DateTimeOffset At,
    string Topic,
    string Opponent,
    bool Won,
    bool Draw,
    int Score,
    FightStyleSnapshot Style);

/// <summary>A fighter: the tag is the identity, and only the display name can be edited.</summary>
public sealed record FighterDto(string Tag, string DisplayName, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt);
