namespace PoFightJudge.Api.Features.Watch;

/// <summary>
/// Computes <see cref="AdvancedStats"/> from raw round text. Pure: no clock, no randomness, no I/O, so the same
/// transcript always scores the same and the numbers on the verdict page can be argued with.
/// </summary>
public static class ArgueScoreCalculator
{
    private static readonly string[] PassiveAggressiveMarkers = ["fine", "whatever", "i guess", "if you say so", "sure", "no problem"];

    private static readonly string[] GrievanceMarkers = ["you always", "you never", "last time", "remember when", "just like"];

    private static readonly string[] BlameMarkers = ["your fault", "because of you", "you made", "you did", "you said"];

    private static readonly string[] FallacyMarkers = ["everyone knows", "nobody", "always", "never", "obviously", "clearly", "prove it"];

    private static readonly string[] ApologyMarkers = ["sorry", "apologize", "forgive"];

    /// <summary>
    /// Scores one speaker. <paramref name="totalWordCount"/> is both speakers' words, so dominance is a share of the
    /// match rather than of themselves.
    /// </summary>
    public static AdvancedStats Compute(IEnumerable<string> roundTexts, int totalWordCount)
    {
        var texts = roundTexts as IReadOnlyList<string> ?? [.. roundTexts];
        var joined = string.Join(" ", texts).ToLowerInvariant();
        var words = joined.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var wordCount = words.Length;

        // Blame feeds both its own metric and the apology ratio's denominator; scan once.
        var blameCount = Count(joined, BlameMarkers);

        return new AdvancedStats(
            PassiveAggressionIndex: Cap(Count(joined, PassiveAggressiveMarkers) / Math.Max(1.0, wordCount) * 100),
            HistoricalGrievanceRate: Cap(Count(joined, GrievanceMarkers) / Math.Max(1.0, wordCount) * 100),
            BlameMetric: Cap(blameCount / Math.Max(1.0, wordCount) * 100),
            LogicalFallacyCount: Count(joined, FallacyMarkers),
            VolumeScore: Cap(Math.Min(100, wordCount / 10.0)),
            WordCountDominance: Cap(totalWordCount == 0 ? 50 : wordCount / (double)totalWordCount * 100),
            EmotionalVolatility: Cap(joined.Count(c => c == '!') / Math.Max(1.0, texts.Count) * 20),
            DeflectionCoefficient: Cap(joined.Count(c => c == '?') / Math.Max(1.0, wordCount) * 100),
            LexicalComplexity: Cap(wordCount == 0 ? 0 : words.Average(w => w.Length) * 10),
            ApologyToInsultRatio: Cap(Count(joined, ApologyMarkers) / Math.Max(1.0, blameCount + 1) * 100));
    }

    /// <summary>Word count of a line, the same way the scorer counts it, so callers can total a match without re-splitting.</summary>
    public static int WordCount(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private static int Count(string text, string[] markers) => markers.Sum(m => CountOccurrences(text, m));

    private static int CountOccurrences(string text, string term)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(term, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += term.Length;
        }

        return count;
    }

    private static double Cap(double value) => Math.Clamp(value, 0, 100);
}
