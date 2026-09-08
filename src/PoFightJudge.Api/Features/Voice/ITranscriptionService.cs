namespace PoFightJudge.Api.Features.Voice;

/// <summary>
/// Speech-to-text for a live human (SELF) turn in WATCH. The endpoint depends on this contract, never on a provider.
/// </summary>
public interface ITranscriptionService
{
    /// <summary>Short stable name for logs and the diag page ("azure-fast", "gemini", "fake").</summary>
    string Name { get; }

    /// <summary>True when this implementation can actually transcribe. Surfaced through <c>/api/features</c> so the client hides the SELF player rather than offering a match it cannot finish.</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Transcribes one recorded turn. <paramref name="wav"/> is a complete WAV container (16 kHz mono PCM16, as the
    /// browser recorder produces). Returns the spoken words, or an empty string when nothing intelligible was said —
    /// a silent turn is a legitimate outcome, not an error.
    /// </summary>
    Task<string> TranscribeAsync(byte[] wav, CancellationToken ct = default);
}

/// <summary>Shared rules for turning a provider's answer into a line the game can use.</summary>
public static class Transcripts
{
    /// <summary>
    /// Hard cap on one turn's audio. 16 kHz mono PCM16 is 32 KB/s, so this is a little over two minutes — far beyond
    /// a spoken argument line, and low enough that a runaway recorder cannot post an unbounded body.
    /// </summary>
    public const int MaxWavBytes = 4 * 1024 * 1024;

    /// <summary>
    /// The token a model is told to emit for a clip with no intelligible speech. Asking for an explicit token rather
    /// than "output nothing" is what actually works: told to stay silent, models narrate instead — two seconds of
    /// digital silence came back as "I'm not sure what you mean." and a 440 Hz tone as "Hello.", both of which would
    /// have been submitted as the player's argument and then judged.
    /// </summary>
    public const string NoSpeechSentinel = "NO_SPEECH";

    private static readonly string[] NonSpeechMarkers =
    [
        NoSpeechSentinel, "no intelligible speech", "no speech", "[inaudible]", "(inaudible)",
        "[silence]", "(silence)", "no audible", "unintelligible",
    ];

    private static readonly string[] Labels = ["Transcript:", "Transcription:"];

    /// <summary>A WAV container starts with RIFF....WAVE; anything else is a recorder bug or a hostile upload.</summary>
    public static bool LooksLikeWav(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WAVE"u8);

    /// <summary>
    /// Strips the wrappers a transcription model still occasionally adds — code fences, a leading "Transcript:"
    /// label, surrounding quotes — so a stray preamble never becomes part of the player's line, and turns the stock
    /// phrases a model reaches for when narrating silence into the empty transcript they actually mean.
    /// </summary>
    public static string Clean(string raw)
    {
        var text = raw.Trim();
        if (text.Length == 0)
        {
            return string.Empty;
        }

        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstBreak = text.IndexOf('\n', StringComparison.Ordinal);
            if (firstBreak >= 0)
            {
                text = text[(firstBreak + 1)..];
            }

            if (text.EndsWith("```", StringComparison.Ordinal))
            {
                text = text[..^3];
            }

            text = text.Trim();
        }

        foreach (var label in Labels)
        {
            if (text.StartsWith(label, StringComparison.OrdinalIgnoreCase))
            {
                text = text[label.Length..].Trim();
                break;
            }
        }

        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
        {
            text = text[1..^1].Trim();
        }

        return IsNonSpeech(text) ? string.Empty : text;
    }

    /// <summary>Length-capped so a real line that merely mentions silence — the player may well argue about the silent treatment — is never mistaken for one.</summary>
    public static bool IsNonSpeech(string text) =>
        text.Length <= 60 && NonSpeechMarkers.Any(m => text.Contains(m, StringComparison.OrdinalIgnoreCase));
}
