using PoFightJudge.Shared.Models;

namespace PoFightJudge.Api.Features.Records;

/// <summary>
/// Elo, replayed from the result rows on every read rather than stored. Nothing is accumulated, so a re-judged
/// fight re-rates everybody after it and a deleted one takes its points with it.
/// </summary>
/// <remarks>
/// A debate can leave one row (a person against a persona) or two (two people, or two personas). Either way it is
/// one bout, rated once from whichever row sorts first; an opponent with no row of their own is rated from it too.
/// </remarks>
public static class EloRatings
{
    public const int Start = 1000;

    /// <summary>How far one bout can move a rating. 32 is the classic club value: a newcomer finds their level in a dozen fights.</summary>
    private const double K = 32;

    public static IReadOnlyDictionary<string, int> Compute(IEnumerable<FighterResultDto> rows) =>
        Replay(rows.Select(r => new Bout(r.MatchId.Value, r.At, r.Tag, r.Opponent, r.Won, r.Draw)));

    public static IReadOnlyDictionary<string, int> Compute(IEnumerable<WatchResultDto> rows) =>
        Replay(rows.Select(r => new Bout(r.MatchId.Value, r.At, r.Initials, r.Opponent, r.Won, r.Draw)));

    private static Dictionary<string, int> Replay(IEnumerable<Bout> rows)
    {
        var ratings = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        var bouts = rows
            .Where(b => !string.IsNullOrWhiteSpace(b.Opponent) && !string.Equals(b.Player, b.Opponent, StringComparison.OrdinalIgnoreCase))
            .GroupBy(b => b.Match, StringComparer.Ordinal)
            .Select(g => g.OrderBy(b => b.Player, StringComparer.OrdinalIgnoreCase).First())
            .OrderBy(b => b.At)
            .ThenBy(b => b.Match, StringComparer.Ordinal);

        foreach (var bout in bouts)
        {
            var mine = ratings.GetValueOrDefault(bout.Player, Start);
            var theirs = ratings.GetValueOrDefault(bout.Opponent, Start);
            var expected = 1 / (1 + Math.Pow(10, (theirs - mine) / 400));
            var actual = bout.Draw ? 0.5 : bout.Won ? 1 : 0;
            var change = K * (actual - expected);

            ratings[bout.Player] = mine + change;
            ratings[bout.Opponent] = theirs - change;
        }

        return ratings.ToDictionary(p => p.Key, p => (int)Math.Round(p.Value), StringComparer.OrdinalIgnoreCase);
    }

    private sealed record Bout(string Match, DateTimeOffset At, string Player, string Opponent, bool Won, bool Draw);
}
