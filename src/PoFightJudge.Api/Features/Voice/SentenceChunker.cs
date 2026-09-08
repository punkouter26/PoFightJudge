namespace PoFightJudge.Api.Features.Voice;

/// <summary>
/// Splits a spoken line into sentence groups worth their own synthesis call. Very short sentences are merged forward
/// so a line of clipped three-word retorts does not turn into six provider round trips; the point is to start
/// playing sooner, not to fan out for its own sake. Word order and content are preserved exactly — this is dialogue.
/// </summary>
public static class SentenceChunker
{
    /// <summary>Below this, a sentence is merged into the next one rather than sent alone.</summary>
    public const int MinChunkChars = 60;

    /// <summary>A line shorter than this is not worth splitting at all.</summary>
    public const int MinSplittableChars = 120;

    public static IReadOnlyList<string> Split(string? text)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return [];
        }

        if (trimmed.Length < MinSplittableChars)
        {
            return [trimmed];
        }

        var sentences = new List<string>();
        var start = 0;
        for (var i = 0; i < trimmed.Length; i++)
        {
            if (trimmed[i] is not ('.' or '!' or '?'))
            {
                continue;
            }

            // Run past "?!" and "..." so a single break does not become three.
            var end = i;
            while (end + 1 < trimmed.Length && trimmed[end + 1] is '.' or '!' or '?')
            {
                end++;
            }

            // A boundary needs whitespace after it; "3.5" and "Mrs." are not sentences.
            if (end + 1 < trimmed.Length && !char.IsWhiteSpace(trimmed[end + 1]))
            {
                i = end;
                continue;
            }

            sentences.Add(trimmed[start..(end + 1)].Trim());
            start = end + 1;
            i = end;
        }

        if (start < trimmed.Length)
        {
            var tail = trimmed[start..].Trim();
            if (tail.Length > 0)
            {
                sentences.Add(tail);
            }
        }

        if (sentences.Count <= 1)
        {
            return [trimmed];
        }

        // Merge runts forward so every chunk is worth a round trip.
        var merged = new List<string>(sentences.Count);
        var buffer = string.Empty;
        foreach (var sentence in sentences)
        {
            buffer = buffer.Length == 0 ? sentence : $"{buffer} {sentence}";
            if (buffer.Length < MinChunkChars)
            {
                continue;
            }

            merged.Add(buffer);
            buffer = string.Empty;
        }

        // A trailing runt joins the previous chunk rather than becoming its own call — "(seething)" is not an utterance.
        if (buffer.Length > 0)
        {
            if (merged.Count > 0)
            {
                merged[^1] = $"{merged[^1]} {buffer}";
            }
            else
            {
                merged.Add(buffer);
            }
        }

        return merged.Count == 0 ? [trimmed] : merged;
    }
}
