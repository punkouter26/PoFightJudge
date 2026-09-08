using System.Text;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Api.Features.Analysis;

/// <summary>
/// Locates the judge's quoted moments in the diarized transcript so each one can be played back as a clip.
/// The judge quotes loosely (it tidies grammar and trims), so matching is fuzzy: the best-scoring window of the
/// speaker's own words wins, and anything below <see cref="MinScore"/> is dropped rather than guessed at.
/// </summary>
public static class HighlightFinder
{
    public const double PaddingSeconds = 0.6;
    public const double MinClipSeconds = 1.5;
    public const double MaxClipSeconds = 15;
    public const double MinScore = 0.6;

    /// <summary>Peak, best and worst moment for each player, in the order they were said.</summary>
    public static IReadOnlyList<HighlightDto> Find(PlayerAssessmentDto player1, PlayerAssessmentDto player2, MappedTranscript transcript)
    {
        List<(Speaker Speaker, string Label, string Quote)> candidates =
        [
            (Speaker.Player1, "Peak moment", player1.PeakMomentQuote),
            (Speaker.Player1, "Best argument", player1.BestMomentQuote),
            (Speaker.Player1, "Worst moment", player1.WorstMomentQuote),
            (Speaker.Player2, "Peak moment", player2.PeakMomentQuote),
            (Speaker.Player2, "Best argument", player2.BestMomentQuote),
            (Speaker.Player2, "Worst moment", player2.WorstMomentQuote),
        ];

        var found = new List<HighlightDto>();
        foreach (var (speaker, label, quote) in candidates)
        {
            if (Locate(speaker, label, quote, transcript) is { } highlight && !Overlaps(found, highlight))
            {
                found.Add(highlight);
            }
        }

        return [.. found.OrderBy(h => h.StartSeconds)];
    }

    /// <summary>The span of <paramref name="speaker"/>'s words that best matches <paramref name="quote"/>, or null if nothing does.</summary>
    public static HighlightDto? Locate(Speaker speaker, string label, string? quote, MappedTranscript transcript)
    {
        var wanted = Tokenize(quote);
        var spoken = transcript.For(speaker).ToList();
        if (wanted.Count == 0 || spoken.Count == 0)
        {
            return null;
        }

        var tokens = spoken.Select(w => Normalize(w.Text)).ToList();
        var window = Math.Min(wanted.Count, spoken.Count);
        var best = -1;
        var bestScore = 0.0;
        for (var i = 0; i + window <= tokens.Count; i++)
        {
            var score = Score(wanted, tokens.GetRange(i, window));
            if (score > bestScore)
            {
                (bestScore, best) = (score, i);
            }
        }

        if (best < 0 || bestScore < MinScore)
        {
            return null;
        }

        var start = Math.Max(0, spoken[best].Start - PaddingSeconds);
        var end = spoken[best + window - 1].End + PaddingSeconds;
        if (end - start < MinClipSeconds)
        {
            end = start + MinClipSeconds;
        }

        if (end - start > MaxClipSeconds)
        {
            end = start + MaxClipSeconds;
        }

        return new HighlightDto(speaker, label, quote!.Trim(), Math.Round(start, 2), Math.Round(end, 2));
    }

    /// <summary>Share of the quote's words that the window also contains, counting duplicates only as often as they occur.</summary>
    private static double Score(List<string> wanted, List<string> window)
    {
        var pool = new List<string>(window);
        var hits = 0;
        foreach (var token in wanted)
        {
            var at = pool.IndexOf(token);
            if (at >= 0)
            {
                pool.RemoveAt(at);
                hits++;
            }
        }

        return (double)hits / wanted.Count;
    }

    /// <summary>Two quotes that land on the same moment produce one clip, not two identical ones.</summary>
    private static bool Overlaps(List<HighlightDto> found, HighlightDto candidate) =>
        found.Any(h => h.Speaker == candidate.Speaker
            && Math.Min(h.EndSeconds, candidate.EndSeconds) - Math.Max(h.StartSeconds, candidate.StartSeconds)
                > (candidate.EndSeconds - candidate.StartSeconds) / 2);

    private static List<string> Tokenize(string? text) =>
        [.. (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize)
            .Where(t => t.Length > 0)];

    private static string Normalize(string word)
    {
        var sb = new StringBuilder(word.Length);
        foreach (var c in word)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }

        return sb.ToString();
    }
}
