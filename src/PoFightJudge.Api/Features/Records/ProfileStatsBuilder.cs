using PoFightJudge.Shared.Models;

namespace PoFightJudge.Api.Features.Records;

/// <summary>
/// A persona's record across the watches it has argued in, computed on read from its result rows. The same rule as
/// a fighter's: nothing accumulates, so re-judging a match corrects the record rather than doubling it.
/// </summary>
public static class ProfileStatsBuilder
{
    public static ProfileRecordDto Build(string initials, IReadOnlyList<WatchResultDto> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        if (results.Count == 0)
        {
            return ProfileRecordDto.Empty(initials);
        }

        var ordered = results.OrderBy(r => r.At).ToList();
        var wins = ordered.Count(r => r.Won);
        var draws = ordered.Count(r => r.Draw);

        return new ProfileRecordDto(
            initials,
            ordered.Count,
            wins,
            ordered.Count - wins - draws,
            draws,
            Math.Round(ordered.Average(r => (double)r.Score), 1),
            ordered[^1].At,
            Means(ordered),
            TopRival(ordered));
    }

    /// <summary>
    /// The averages of everything the scorer measures. A persona's character is supposed to show here: one that
    /// deflects always deflects, and the number is what proves it rather than the description that was typed in.
    /// </summary>
    private static AdvancedStatsDto Means(IReadOnlyList<WatchResultDto> ordered) => new(
        Math.Round(ordered.Average(r => r.Stats.PassiveAggressionIndex), 1),
        Math.Round(ordered.Average(r => r.Stats.HistoricalGrievanceRate), 1),
        Math.Round(ordered.Average(r => r.Stats.BlameMetric), 1),
        (int)Math.Round(ordered.Average(r => (double)r.Stats.LogicalFallacyCount)),
        Math.Round(ordered.Average(r => r.Stats.VolumeScore), 1),
        Math.Round(ordered.Average(r => r.Stats.WordCountDominance), 1),
        Math.Round(ordered.Average(r => r.Stats.EmotionalVolatility), 1),
        Math.Round(ordered.Average(r => r.Stats.DeflectionCoefficient), 1),
        Math.Round(ordered.Average(r => r.Stats.LexicalComplexity), 1),
        Math.Round(ordered.Average(r => r.Stats.ApologyToInsultRatio), 1));

    private static RivalryDto? TopRival(IReadOnlyList<WatchResultDto> ordered)
    {
        var rival = ordered
            .Where(r => !string.IsNullOrWhiteSpace(r.Opponent))
            .GroupBy(r => r.Opponent, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .FirstOrDefault();

        if (rival is null)
        {
            return null;
        }

        var wins = rival.Count(r => r.Won);
        var draws = rival.Count(r => r.Draw);
        return new RivalryDto(rival.Key, rival.Count(), wins, rival.Count() - wins - draws);
    }
}
