using PoMarriedFight.Shared;
using PoMarriedFight.Shared.Identifiers;
using PoMarriedFight.Shared.Models;

namespace PoMarriedFight.Api.Features.Watch;

/// <summary>
/// One line of a WATCH match. Immutable once created. Audio is held in memory only long enough to be written to a
/// blob; the stored turn keeps the blob name, never the bytes.
/// </summary>
/// <param name="AudioFormat">
/// Wire format of <paramref name="AudioBase64"/>. Defaults to PCM because that is what an unlabelled round actually
/// is, and a round re-saved from an old row must never be relabelled as something it is not.
/// </param>
public sealed record WatchRound(
    string Speaker,
    string Text,
    string Mood,
    string? AudioBase64 = null,
    string? AudioBlobName = null,
    string AudioFormat = "pcm")
{
    /// <summary>Wire values for <see cref="Speaker"/>. The husband/wife pair is the WATCH vocabulary; storage maps them onto Player1/Player2.</summary>
    public const string Husband = "husband";

    public const string Wife = "wife";

    public bool IsHusband => string.Equals(Speaker, Husband, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Aggregate root for a finished WATCH match. A match only ever exists in its completed form: the round loop plays
/// out on the client, and the verdict endpoint persists the whole thing in one step through <see cref="Complete"/>.
/// </summary>
public sealed class WatchMatch
{
    /// <summary>Per-speaker round cap: husband and wife alternate three each, six lines in all.</summary>
    public const int MaxRoundsPerMatch = 3;

    /// <summary>
    /// A slap interrupts one line and inserts an extra reaction, and it is available once per match — so a
    /// legitimate match can carry seven lines, not six. Without this allowance every slapped match failed at the
    /// verdict, and "retry" could never succeed because it rebuilt the same seven-line payload.
    /// </summary>
    public const int MaxLinesPerMatch = (MaxRoundsPerMatch * 2) + 1;

    private readonly List<WatchRound> _rounds = [];

    private WatchMatch()
    {
    }

    public MatchId Id { get; private set; }

    public ProfileId Husband { get; private set; }

    public ProfileId Wife { get; private set; }

    public string UserId { get; private set; } = string.Empty;

    public string? Topic { get; private set; }

    /// <summary>The winner's initials, or empty when the judge called neither side.</summary>
    public string Winner { get; private set; } = string.Empty;

    public string Verdict { get; private set; } = string.Empty;

    public DateTimeOffset EndedAt { get; private set; }

    public AdvancedStats HusbandStats { get; private set; } = AdvancedStats.Empty;

    public AdvancedStats WifeStats { get; private set; } = AdvancedStats.Empty;

    public IReadOnlyList<WatchRound> Rounds => _rounds;

    /// <summary>True when the live human played a side: the match is an exhibition and nothing about it is recorded.</summary>
    public bool IsExhibition => SelfPlayer.InMatch(Husband, Wife);

    /// <summary>
    /// Builds a finished match in one step. The line cap is enforced here; the endpoint validates the request first,
    /// so a bad payload is a 400 rather than a 500.
    /// </summary>
    public static WatchMatch Complete(
        MatchId id,
        ProfileId husband,
        ProfileId wife,
        string userId,
        string? topic,
        IReadOnlyList<WatchRound> rounds,
        string winner,
        string verdict,
        AdvancedStats husbandStats,
        AdvancedStats wifeStats,
        DateTimeOffset endedAt)
    {
        ArgumentNullException.ThrowIfNull(rounds);
        if (rounds.Count == 0)
        {
            throw new InvalidOperationException("A match needs at least one round.");
        }

        if (rounds.Count > MaxLinesPerMatch)
        {
            throw new InvalidOperationException($"A match carries at most {MaxLinesPerMatch} lines.");
        }

        var match = new WatchMatch
        {
            Id = id,
            Husband = husband,
            Wife = wife,
            UserId = userId,
            Topic = topic,
            Winner = winner,
            Verdict = verdict,
            HusbandStats = husbandStats,
            WifeStats = wifeStats,
            EndedAt = endedAt,
        };
        match._rounds.AddRange(rounds);
        return match;
    }

    /// <summary>Full hydration for the repository.</summary>
    public static WatchMatch Rehydrate(
        MatchId id,
        ProfileId husband,
        ProfileId wife,
        string userId,
        string? topic,
        string winner,
        string verdict,
        DateTimeOffset endedAt,
        IEnumerable<WatchRound> rounds,
        AdvancedStats husbandStats,
        AdvancedStats wifeStats)
    {
        var match = new WatchMatch
        {
            Id = id,
            Husband = husband,
            Wife = wife,
            UserId = userId,
            Topic = topic,
            Winner = winner,
            Verdict = verdict,
            EndedAt = endedAt,
            HusbandStats = husbandStats,
            WifeStats = wifeStats,
        };
        match._rounds.AddRange(rounds);
        return match;
    }

    /// <summary>Which side won, as the results tables record it. Null when the judge called neither.</summary>
    public ProfileId? WinnerId() =>
        string.Equals(Winner, Husband.Value, StringComparison.OrdinalIgnoreCase) ? Husband
        : string.Equals(Winner, Wife.Value, StringComparison.OrdinalIgnoreCase) ? Wife
        : null;
}
