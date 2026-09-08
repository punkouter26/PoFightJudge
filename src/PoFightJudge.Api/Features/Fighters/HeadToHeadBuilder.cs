using PoFightJudge.Shared.Models;

namespace PoFightJudge.Api.Features.Fighters;

/// <summary>
/// The series between two people, computed from the same result rows their records are. Nothing is accumulated: a
/// fight that is deleted, or re-read and rescored, changes this on the next read exactly as it changes both pages.
/// </summary>
public static class HeadToHeadBuilder
{
    /// <summary>How many of the meetings the page lists. The rest are counted, not printed.</summary>
    public const int RecentCount = 10;

    /// <summary>
    /// One row per person per match, so the two sides are joined on the match id. The opponent's row is what carries
    /// their score; without it the page could show one number and call it a comparison.
    /// </summary>
    public static HeadToHeadDto Build(
        FighterDto fighter,
        FighterDto opponent,
        IReadOnlyList<FighterResultDto> theirs,
        IReadOnlyList<FighterResultDto> opponentRows)
    {
        ArgumentNullException.ThrowIfNull(fighter);
        ArgumentNullException.ThrowIfNull(opponent);
        ArgumentNullException.ThrowIfNull(theirs);
        ArgumentNullException.ThrowIfNull(opponentRows);

        var against = theirs
            .Where(r => string.Equals(r.Opponent, opponent.Tag, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var byMatch = opponentRows.ToLookup(r => r.MatchId);

        var meetings = against
            .OrderByDescending(r => r.At)
            .Select(r => new MeetingDto(
                r.MatchId,
                r.Mode,
                r.At,
                r.Topic,
                r.Draw ? string.Empty : r.Won ? fighter.Tag : opponent.Tag,
                r.Score,
                byMatch[r.MatchId].FirstOrDefault()?.Score ?? 0))
            .ToList();

        return new HeadToHeadDto(
            fighter.Tag,
            fighter.DisplayName,
            opponent.Tag,
            opponent.DisplayName,
            against.Count,
            against.Count(r => r.Won && !r.Draw),
            against.Count(r => !r.Won && !r.Draw),
            against.Count(r => r.Draw),
            Average(against.Select(r => r.Score)),
            Average(meetings.Select(m => m.OpponentScore)),
            [.. meetings.Take(RecentCount)]);
    }

    /// <summary>Zero rather than NaN when they have never met: the page reads it as a number either way.</summary>
    private static double Average(IEnumerable<int> scores)
    {
        var values = scores.ToList();
        return values.Count == 0 ? 0 : Math.Round(values.Average(), 1);
    }
}
