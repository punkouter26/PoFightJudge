using System.Globalization;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Api.Features.Fighters;

/// <summary>
/// What one debate says about how a person argues, boiled down to the handful of things worth remembering. Every
/// snapshot is stored with the result that produced it, so a style profile is a read over rows rather than a
/// re-analysis of old recordings.
/// </summary>
/// <remarks>
/// It is deliberately shallow. Anything richer would be re-judging the fight from its own report; the point here is
/// to keep the few facts that are still true a month later — how they sound, what they always say, how they open.
/// </remarks>
public static class FightStyleSnapshotExtractor
{
    public const int MaxPhrases = 3;
    public const int MaxFallacies = 3;
    public const int MaxEmotions = 3;
    public const int MaxTips = 3;
    public const int OpenerWords = 12;
    public const int MaxQuoteLength = 120;

    /// <summary>From a judged fight: the assessment says how they came across, the metrics what they kept saying.</summary>
    public static StyleSnapshot FromFight(PlayerAssessmentDto assessment, PlayerMetricsDto metrics, string? opener)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        ArgumentNullException.ThrowIfNull(metrics);

        return new StyleSnapshot(
            Tone: string.Join(", ", assessment.ToneDescriptors.Take(MaxEmotions)),
            Phrases: [.. metrics.RepeatedPhrases.Take(MaxPhrases)],
            Fallacies: [.. assessment.Fallacies.Select(f => f.Name).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxFallacies)],
            Opener: Opener(opener),
            Cefr: assessment.Cefr,
            Emotions: TopEmotions(assessment.Emotions),
            BestQuote: Quote(assessment.BestMomentQuote),
            Tips: [.. assessment.CoachingTips.Take(MaxTips)]);
    }

    /// <summary>
    /// From a watch, where there is no judge report per side — only the lines the person actually spoke. Tone,
    /// fallacies and level are left empty rather than guessed at: an empty field reads as "not known", and a made-up
    /// one would go into their profile as though it had been measured.
    /// </summary>
    public static StyleSnapshot FromSpokenLines(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var said = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (said.Count == 0)
        {
            return StyleSnapshot.Empty;
        }

        return StyleSnapshot.Empty with
        {
            Phrases = RepeatedPhrases(said),
            Opener = Opener(said[0]),
            BestQuote = Quote(said.OrderByDescending(l => l.Length).First()),
        };
    }

    /// <summary>The first few words of the first thing they said. People open the same way every time.</summary>
    public static string Opener(string? line)
    {
        var words = (line ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return words.Length == 0 ? string.Empty : string.Join(' ', words.Take(OpenerWords));
    }

    /// <summary>The emotions that actually showed, strongest first, ignoring the ones that barely registered.</summary>
    private static IReadOnlyList<string> TopEmotions(EmotionProfileDto emotions)
    {
        (string Name, int Share)[] all =
        [
            ("calm", emotions.Calm),
            ("confident", emotions.Confident),
            ("frustrated", emotions.Frustrated),
            ("angry", emotions.Angry),
            ("anxious", emotions.Anxious),
            ("amused", emotions.Amused),
        ];

        return [.. all.Where(e => e.Share > 0).OrderByDescending(e => e.Share).ThenBy(e => e.Name, StringComparer.Ordinal).Take(MaxEmotions).Select(e => e.Name)];
    }

    /// <summary>Three-word runs said more than once, minus the ones everybody says.</summary>
    private static IReadOnlyList<string> RepeatedPhrases(IEnumerable<string> lines)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var words = line.ToLowerInvariant()
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(w => w.Trim('.', ',', '!', '?', ';', ':', '"'))
                .Where(w => w.Length > 0)
                .ToList();

            for (var i = 0; i + 2 < words.Count; i++)
            {
                var phrase = $"{words[i]} {words[i + 1]} {words[i + 2]}";
                counts[phrase] = counts.GetValueOrDefault(phrase) + 1;
            }
        }

        return
        [
            .. counts.Where(kv => kv.Value >= 2)
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Take(MaxPhrases)
                .Select(kv => string.Create(CultureInfo.InvariantCulture, $"{kv.Key} (×{kv.Value})")),
        ];
    }

    /// <summary>A quote worth keeping: trimmed, and short enough to sit in a table.</summary>
    private static string Quote(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length <= MaxQuoteLength ? trimmed : trimmed[..MaxQuoteLength].TrimEnd() + "…";
    }
}
