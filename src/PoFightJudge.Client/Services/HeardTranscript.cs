using System.Globalization;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Client.Services;

/// <summary>
/// Collects what the browser's recogniser heard during a fight into the word-level transcript the analysis reads.
/// </summary>
/// <remarks>
/// The server half of this has existed since T41 — <c>AcceptClientTranscript</c>, <c>ClientTranscript.Sanitize</c>,
/// the endpoint, the wait in the pipeline — and nothing ever posted to it, because the browser half was never
/// written. The point is that it is free: the analysis prefers the host's own live captions and, below
/// <c>MinCaptionTranscriptWords</c>, pays a model to diarize the recording. This is a second free source to try
/// before that one.
///
/// The Web Speech API reports no word timings, only settled utterances. Offsets are therefore interpolated evenly
/// across each utterance between when the recogniser began hearing it and when it settled. That is an estimate and
/// is treated as one: the speaker mapper attributes words by overlapping them with the turn timeline, which is
/// where the truth about who held the floor actually lives.
/// </remarks>
public sealed class HeardTranscript
{
    /// <summary>
    /// The same ceiling the endpoint enforces on the way in, applied here so a long fight never builds a body the
    /// server is only going to refuse.
    /// </summary>
    public const int MaxWords = 20_000;

    /// <summary>
    /// One label for everything. Both fighters share a microphone, so the browser cannot tell them apart, and a
    /// made-up second speaker would only give the mapper something wrong to believe.
    /// </summary>
    public const string Label = "spk_1";

    private readonly List<TranscriptWord> _words = [];

    public int Count => _words.Count;

    /// <summary>One settled utterance, with the window of the fight it was heard in.</summary>
    public void Heard(string text, double startSeconds, double endSeconds)
    {
        if (_words.Count >= MaxWords)
        {
            return;
        }

        var spoken = (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (spoken.Length == 0)
        {
            return;
        }

        // A recogniser that reported the end before the start, or in the same instant, is still an utterance; it
        // just has no span to spread across.
        var from = Math.Max(0, Math.Min(startSeconds, endSeconds));
        var to = Math.Max(from, Math.Max(startSeconds, endSeconds));
        var each = (to - from) / spoken.Length;

        for (var i = 0; i < spoken.Length && _words.Count < MaxWords; i++)
        {
            var wordStart = from + (each * i);
            _words.Add(new TranscriptWord(spoken[i], Label, wordStart, wordStart + each));
        }
    }

    /// <summary>Everything heard, or null when nothing was — which is not a transcript worth posting.</summary>
    public TranscriptDto? ToTranscript() =>
        _words.Count == 0
            ? null
            : new TranscriptDto(string.Join(' ', _words.Select(w => w.Text)), [.. _words]);

    /// <summary>For the log line that says which source a fight ended up with.</summary>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{_words.Count} words heard in the browser");
}
