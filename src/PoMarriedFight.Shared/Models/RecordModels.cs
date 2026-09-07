using System.Text.Json.Serialization;
using PoMarriedFight.Shared.Identifiers;

namespace PoMarriedFight.Shared.Models;

/// <summary>What is occupying a side of a match.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SideKind>))]
public enum SideKind
{
    /// <summary>An authored AI persona from the Profiles cast.</summary>
    Persona,

    /// <summary>A real person speaking into the microphone, identified by their fighter tag.</summary>
    Human,
}

/// <summary>
/// One side of a match: who it is, what to call them, and which kind they are. Both engines use this, because a
/// person keeps one identity whether they argue a persona or another person — their tag is the same either way.
/// </summary>
public sealed record MatchSide(string Id, string DisplayName, SideKind Kind)
{
    public bool IsHuman => Kind == SideKind.Human;

    /// <summary>An authored persona, named by its initials.</summary>
    public static MatchSide Persona(string initials, string? displayName = null) =>
        new(initials, string.IsNullOrWhiteSpace(displayName) ? initials : displayName, SideKind.Persona);

    /// <summary>A live person, named by their fighter tag. The tag is the identity; the display name is theirs to change.</summary>
    public static MatchSide Human(string tag, string? displayName = null) =>
        new(tag, string.IsNullOrWhiteSpace(displayName) ? tag : displayName, SideKind.Human);
}

/// <summary>
/// One match, either mode. The two engines meet here: a WATCH match pairs two personas, or a persona and a live
/// person; a FIGHT pairs two live people. Owner, topic, timing, outcome and whether the AI was faked are shared, so
/// history and deletion are one query and one cascade rather than two of each.
/// </summary>
public sealed record MatchDto(
    MatchId Id,
    string UserId,
    MatchMode Mode,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string Topic,
    MatchSide Side1,
    MatchSide Side2,
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

    /// <summary>Every side a real person argued, so their record and style profile can be updated from this match.</summary>
    public IEnumerable<MatchSide> HumanSides => new[] { Side1, Side2 }.Where(s => s.IsHuman);
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
/// One persona's record of one WATCH match. Written per side, so a profile's whole record is a single-partition read
/// and re-judging a match overwrites rather than double-counting.
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
/// How a person argued in one debate, kept alongside their result so the style profile never has to re-read a
/// report. Everything here is derived from what they actually said; nothing they type goes in it.
/// </summary>
public sealed record StyleSnapshot(
    string Tone,
    IReadOnlyList<string> Phrases,
    IReadOnlyList<string> Fallacies,
    string Opener,
    string Cefr,
    IReadOnlyList<string> Emotions,
    string BestQuote,
    IReadOnlyList<string> Tips)
{
    public static StyleSnapshot Empty { get; } = new(string.Empty, [], [], string.Empty, string.Empty, [], string.Empty, []);
}

/// <summary>
/// One person's record of one debate they spoke in, either mode, with the style snapshot their profile is built
/// from. A person accumulates these from every match they argued in, whether the opponent was a persona or another
/// person — which is what makes the profile theirs rather than the mode's.
/// </summary>
public sealed record FighterResultDto(
    string Tag,
    MatchId MatchId,
    MatchMode Mode,
    DateTimeOffset At,
    string Topic,
    string Opponent,
    bool Won,
    bool Draw,
    int Score,
    StyleSnapshot Style);

/// <summary>A person who has argued at least once. The tag is the identity; only the display name can be edited.</summary>
public sealed record FighterDto(string Tag, string DisplayName, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt);

/// <summary>Head-to-head with one particular opponent: who they are, and how it has gone.</summary>
public sealed record RivalryDto(string Opponent, int Fights, int Wins, int Losses)
{
    public bool IsAhead => Wins > Losses;
}

/// <summary>
/// One person's record, computed from their result rows rather than accumulated on a counter. Re-reading a fight
/// overwrites its row, so nothing can be counted twice and a deleted fight simply stops existing.
/// </summary>
public sealed record FighterStatsDto(
    string Tag,
    string DisplayName,
    int Fights,
    int Wins,
    int Losses,
    int Draws,
    int Watches,
    double AverageScore,
    int BestScore,
    DateTimeOffset? LastFoughtAt,

    /// <summary>Length of the current run: positive on a winning streak, negative on a losing one.</summary>
    int Streak,

    /// <summary>How alike their scores are, 0 to 1. One is somebody who argues the same way every time.</summary>
    double Consistency,

    /// <summary>Their last few scores, oldest first, for a line that shows where they are heading.</summary>
    IReadOnlyList<int> Form,

    IReadOnlyList<string> Badges,
    RivalryDto? TopRival)
{
    public double WinRate => Fights == 0 ? 0 : (double)Wins / Fights;

    public static FighterStatsDto Empty(string tag, string displayName) =>
        new(tag, displayName, 0, 0, 0, 0, 0, 0, 0, null, 0, 0, [], [], null);
}

/// <summary>
/// How somebody argues, gathered from every debate they have spoken in. Empty until they have argued; thin, and
/// honest about being thin, until they have argued a few times.
/// </summary>
public sealed record StyleProfileDto(
    string Tag,
    int Debates,
    string Tone,
    IReadOnlyList<string> Phrases,
    IReadOnlyList<string> Fallacies,
    string Opener,
    int OpenerRepeats,
    string Cefr,
    IReadOnlyList<string> Emotions,
    string BestQuote,
    IReadOnlyList<string> Tips,

    /// <summary>One line for the host to read before the fight starts. Short enough to sit in a prompt.</summary>
    string Digest)
{
    /// <summary>True while there is too little here to call it a pattern.</summary>
    public bool IsEarly => Debates < 3;

    public static StyleProfileDto Empty(string tag) =>
        new(tag, 0, string.Empty, [], [], string.Empty, 0, string.Empty, [], string.Empty, [], "First fight.");
}

/// <summary>Everything one fighter's page shows: who they are, their record, and how they argue.</summary>
public sealed record FighterProfileDto(FighterDto Fighter, FighterStatsDto Stats, StyleProfileDto Style, IReadOnlyList<FighterResultDto> Results);

/// <summary>One persona's record from the watches they have argued in.</summary>
public sealed record ProfileRecordDto(
    string Initials,
    int Matches,
    int Wins,
    int Losses,
    int Draws,
    double AverageScore,
    DateTimeOffset? LastSeenAt,
    AdvancedStatsDto AverageStats,
    RivalryDto? TopRival)
{
    public double WinRate => Matches == 0 ? 0 : (double)Wins / Matches;

    public static ProfileRecordDto Empty(string initials) =>
        new(initials, 0, 0, 0, 0, 0, null, AdvancedStatsDto.Empty, null);
}

/// <summary>One row of a leaderboard, whichever kind it is.</summary>
public sealed record LeaderboardRowDto(string Id, string DisplayName, int Matches, int Wins, double WinRate, double AverageScore);
