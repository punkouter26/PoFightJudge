using PoMarriedFight.Shared.Models;

namespace PoMarriedFight.Api.Features.Fighters;

/// <summary>
/// One person's record, computed on read from the rows their debates left behind. Nothing is accumulated on a
/// counter: re-reading a fight overwrites its row, so a corrected result corrects the record instead of being
/// counted twice, and a deleted fight simply stops existing.
/// </summary>
public static class FighterStatsBuilder
{
    /// <summary>How many recent scores a form line shows. Enough to see a direction, few enough to fit.</summary>
    public const int FormLength = 8;

    /// <summary>Below this, a record is a handful of results rather than a pattern, and badges are withheld.</summary>
    public const int MinimumForBadges = 3;

    public static FighterStatsDto Build(FighterDto fighter, IReadOnlyList<FighterResultDto> results)
    {
        ArgumentNullException.ThrowIfNull(fighter);
        ArgumentNullException.ThrowIfNull(results);

        if (results.Count == 0)
        {
            return FighterStatsDto.Empty(fighter.Tag, fighter.DisplayName);
        }

        var ordered = results.OrderBy(r => r.At).ToList();
        var wins = ordered.Count(r => r.Won);
        var draws = ordered.Count(r => r.Draw);

        var stats = new FighterStatsDto(
            fighter.Tag,
            fighter.DisplayName,
            ordered.Count,
            wins,
            ordered.Count - wins - draws,
            draws,
            ordered.Count(r => r.Mode == MatchMode.Watch),
            Math.Round(ordered.Average(r => (double)r.Score), 1),
            ordered.Max(r => r.Score),
            ordered[^1].At,
            Streak(ordered),
            Consistency(ordered),
            [.. ordered.TakeLast(FormLength).Select(r => r.Score)],
            [],
            TopRival(ordered));

        return stats with { Badges = Badges(stats, ordered) };
    }

    /// <summary>
    /// The current run, counted back from the most recent result. Draws end a run without starting one: neither
    /// side of a draw is on a streak.
    /// </summary>
    private static int Streak(List<FighterResultDto> ordered)
    {
        var last = ordered[^1];
        if (last.Draw)
        {
            return 0;
        }

        var run = 0;
        for (var i = ordered.Count - 1; i >= 0; i--)
        {
            if (ordered[i].Draw || ordered[i].Won != last.Won)
            {
                break;
            }

            run++;
        }

        return last.Won ? run : -run;
    }

    /// <summary>
    /// How alike their scores are, from nought to one. Somebody who scores the same every time is consistent;
    /// somebody who swings between brilliant and hopeless is not, and that is worth knowing about them.
    /// </summary>
    private static double Consistency(List<FighterResultDto> ordered)
    {
        if (ordered.Count < 2)
        {
            return 0;
        }

        var scores = ordered.Select(r => (double)r.Score).ToList();
        var mean = scores.Average();
        var deviation = Math.Sqrt(scores.Sum(s => (s - mean) * (s - mean)) / scores.Count);

        // Scores run 0 to 100, so half the range is as spread out as anybody realistically gets.
        return Math.Round(Math.Clamp(1 - (deviation / 50.0), 0, 1), 2);
    }

    /// <summary>Whoever they argue with most, and how that has gone. Nobody has a rival after one debate.</summary>
    private static RivalryDto? TopRival(List<FighterResultDto> ordered)
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

    /// <summary>
    /// What is worth saying about somebody at a glance. Held back until there are enough debates to mean it: a
    /// badge earned from one lucky night is a badge that says nothing.
    /// </summary>
    private static List<string> Badges(FighterStatsDto stats, List<FighterResultDto> ordered)
    {
        if (stats.Fights < MinimumForBadges)
        {
            return [];
        }

        var badges = new List<string>();

        if (stats.WinRate >= 0.7)
        {
            badges.Add("Usually right");
        }

        if (stats.WinRate <= 0.3)
        {
            badges.Add("Keeps trying");
        }

        if (stats.Streak >= 3)
        {
            badges.Add($"On a run of {stats.Streak}");
        }

        if (stats.Streak <= -3)
        {
            badges.Add($"Lost {-stats.Streak} in a row");
        }

        if (stats.Consistency >= 0.85)
        {
            badges.Add("Same every time");
        }

        if (stats.BestScore >= 80)
        {
            badges.Add("Has had a great night");
        }

        if (ordered.Count(r => r.Mode == MatchMode.Watch) == ordered.Count)
        {
            badges.Add("Only argues with the cast");
        }

        if (ordered.SelectMany(r => r.Style.Fallacies).Any())
        {
            var worst = ordered.SelectMany(r => r.Style.Fallacies)
                .GroupBy(f => f, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.Ordinal)
                .First();

            if (worst.Count() >= 2)
            {
                badges.Add($"Reaches for the {worst.Key.ToLowerInvariant()}");
            }
        }

        return badges;
    }
}
