using PoFightJudge.Shared.Models;

namespace PoFightJudge.Api.Features.Records;

/// <summary>
/// A profile's record, computed from result rows on read like every other record here: the cast from the watches
/// they were argued in, people from the fights they had. Keyed by initials (a tag is a person's initials too).
/// </summary>
public static class ProfileRecords
{
    public static IReadOnlyDictionary<string, ProfileRecordDto> From(IReadOnlyCollection<WatchResultDto> rows) =>
        Count(rows.Select(r => (r.Initials, r.Won, r.Draw)), EloRatings.Compute(rows));

    public static IReadOnlyDictionary<string, ProfileRecordDto> From(IReadOnlyCollection<FighterResultDto> rows) =>
        Count(rows.Select(r => (r.Tag, r.Won, r.Draw)), EloRatings.Compute(rows));

    private static Dictionary<string, ProfileRecordDto> Count(IEnumerable<(string Key, bool Won, bool Draw)> rows, IReadOnlyDictionary<string, int> ratings) =>
        rows.GroupBy(r => r.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(
            g => g.Key,
            g =>
            {
                var wins = g.Count(r => r.Won);
                var draws = g.Count(r => r.Draw);
                return new ProfileRecordDto(g.Count(), wins, g.Count() - wins - draws, draws, ratings.GetValueOrDefault(g.Key, EloRatings.Start));
            },
            StringComparer.OrdinalIgnoreCase);
}
