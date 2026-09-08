using System.Globalization;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Api.Features.Fight;

/// <summary>
/// Builds a word-level transcript out of the Live API's own input captions — text the show already pays for and
/// already puts on screen. It stands in for a separate diarization call: the orchestrator knows who holds the floor,
/// so the speaker label comes from the turn state rather than from a diarizer's guess at who is who.
/// </summary>
/// <remarks>
/// The trade is precision. Captions arrive in chunks rather than words, so offsets are interpolated across each
/// chunk's arrival window and land within about a second instead of on the word. Labels keep the same shape a
/// diarizer produces, so the speaker mapper reads either source unchanged.
/// </remarks>
public sealed class CaptionTranscript
{
    /// <summary>Captions arrive after the words were spoken; offsets are shifted back by this much before use.</summary>
    public const double LagSeconds = 0.5;

    /// <summary>A chunk landing after a long silence is assumed to cover at most this much speech.</summary>
    public const double MaxChunkSeconds = 15.0;

    /// <summary>Floor on an interpolated word, so every word has a span the speaker mapper can overlap.</summary>
    public const double MinWordSeconds = 0.05;

    public const int MaxChunks = 4_000;
    public const int MaxWords = 20_000;

    /// <summary>Speech heard while nobody held the floor: the introduction, or the two of them talking over each other.</summary>
    public const string UnknownLabel = "live_x";

    private readonly List<(string Text, string Label, double At)> _chunks = [];

    public int Count => _chunks.Count;

    /// <summary>One caption chunk, stamped with the elapsed time it arrived and whoever had the floor then.</summary>
    public void Add(string? text, Speaker? speaker, double atSeconds)
    {
        if (string.IsNullOrWhiteSpace(text) || _chunks.Count >= MaxChunks || !double.IsFinite(atSeconds))
        {
            return;
        }

        _chunks.Add((text, Label(speaker), Math.Max(0, atSeconds)));
    }

    /// <summary>The transcript, or null when the host never heard a word worth keeping.</summary>
    public TranscriptDto? Build()
    {
        var words = new List<TranscriptWord>();
        var previousEnd = 0.0;

        foreach (var (text, label, at) in _chunks)
        {
            var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            // The chunk covers everything since the last one, bounded so a long silence does not stretch it.
            var end = Math.Max(0, at - LagSeconds);
            var start = Math.Clamp(end - MaxChunkSeconds, 0, end);
            if (previousEnd > start)
            {
                start = Math.Min(previousEnd, end);
            }

            var perWord = Math.Max(MinWordSeconds, (end - start) / parts.Length);
            foreach (var part in parts)
            {
                if (words.Count >= MaxWords)
                {
                    break;
                }

                words.Add(new TranscriptWord(part, label, Math.Round(start, 2), Math.Round(start + perWord, 2)));
                start += perWord;
            }

            previousEnd = start;
        }

        return words.Count == 0
            ? null
            : new TranscriptDto(string.Join(' ', words.Select(w => w.Text)), words);
    }

    /// <summary>Elapsed seconds since the fight started: the offset base every word is expressed in.</summary>
    public static double Elapsed(DateTimeOffset now, DateTimeOffset startedAt) =>
        Math.Round(Math.Max(0, (now - startedAt).TotalSeconds), 2);

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{_chunks.Count} caption chunks");

    private static string Label(Speaker? speaker) => speaker switch
    {
        Speaker.Player1 => "live_1",
        Speaker.Player2 => "live_2",
        _ => UnknownLabel,
    };
}
