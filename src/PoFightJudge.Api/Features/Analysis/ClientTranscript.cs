using PoFightJudge.Shared.Models;

namespace PoFightJudge.Api.Features.Analysis;

/// <summary>
/// Bounds a transcript a browser posted, before it is stored. It reaches the judge's prompt as data, so the only
/// things that matter here are size and shape: no unbounded strings, no absurd offsets, nothing free-text that
/// nothing downstream reads.
/// </summary>
public static class ClientTranscript
{
    public const int MaxWords = 20_000;
    public const int MaxWordLength = 64;
    public const int MaxLabelLength = 16;

    /// <summary>Longer than any recording this app will produce, plus slack. Past it, an offset is simply wrong.</summary>
    public const double MaxSeconds = 3_600;

    /// <summary>A bounded copy, or null when there is nothing usable in it.</summary>
    public static TranscriptDto? Sanitize(TranscriptDto? transcript)
    {
        if (transcript is null || transcript.Words.Count is 0 or > MaxWords)
        {
            return null;
        }

        var words = new List<TranscriptWord>(transcript.Words.Count);
        foreach (var word in transcript.Words)
        {
            if (Clean(word) is { } cleaned)
            {
                words.Add(cleaned);
            }
        }

        // The text is rebuilt rather than trusted: nothing downstream reads the posted one, and it would otherwise
        // be an unbounded string in a stored blob.
        return words.Count == 0 ? null : new TranscriptDto(string.Join(' ', words.Select(w => w.Text)), words);
    }

    private static TranscriptWord? Clean(TranscriptWord word)
    {
        var text = (word.Text ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return null;
        }

        if (text.Length > MaxWordLength)
        {
            text = text[..MaxWordLength];
        }

        var label = (word.Label ?? string.Empty).Trim();
        label = label.Length switch
        {
            0 => "spk_1",
            > MaxLabelLength => label[..MaxLabelLength],
            _ => label,
        };

        var start = Offset(word.Start);
        var end = Offset(word.End);
        return new TranscriptWord(text, label, start, end < start ? start : end);
    }

    private static double Offset(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, MaxSeconds) : 0;
}
