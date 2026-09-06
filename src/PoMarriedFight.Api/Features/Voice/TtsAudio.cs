namespace PoMarriedFight.Api.Features.Voice;

/// <summary>
/// One synthesized utterance and the wire format it is actually in. The format travels with the bytes because the
/// provider chain is a fallback chain: a request that asked for mp3 can still come back as PCM when Fish is down and
/// Gemini TTS — which only emits raw PCM — answers instead. The client decodes on this value, never on what it asked for.
/// </summary>
/// <param name="Base64">Base64-encoded audio bytes; empty when nothing was synthesized.</param>
/// <param name="Format">One of <see cref="TtsAudioFormats"/>.</param>
public readonly record struct TtsAudio(string Base64, string Format)
{
    /// <summary>Nothing was synthesized. Callers treat this as "fall back to the browser voice".</summary>
    public static TtsAudio None { get; } = new(string.Empty, TtsAudioFormats.Pcm);

    public bool IsEmpty => string.IsNullOrEmpty(Base64);

    /// <summary>Raw 24 kHz PCM, what Gemini TTS returns.</summary>
    public static TtsAudio Pcm(string base64) => new(base64, TtsAudioFormats.Pcm);

    public static TtsAudio Mp3(string base64) => new(base64, TtsAudioFormats.Mp3);
}

/// <summary>
/// The wire formats the audio pipeline understands end to end — provider request, blob cache key, JSON payload and
/// the browser decode path all use these exact strings, so a new format is added here and nowhere else.
/// </summary>
public static class TtsAudioFormats
{
    /// <summary>Raw 24 kHz / 16-bit / mono little-endian PCM — no container, no header.</summary>
    public const string Pcm = "pcm";

    /// <summary>MPEG audio, roughly a tenth of the PCM size; decoded by the browser.</summary>
    public const string Mp3 = "mp3";

    public const int PcmSampleRate = 24_000;

    public const int PcmBytesPerSample = 2;

    public static string Extension(string format) => IsMp3(format) ? "mp3" : "pcm";

    /// <summary>MIME type the browser needs in order to decode the payload.</summary>
    public static string MimeType(string format) => IsMp3(format) ? "audio/mpeg" : "audio/pcm";

    /// <summary>Normalizes a configured value to a supported format, defaulting to <see cref="Mp3"/>: an unrecognised value is a typo, not a request for silence.</summary>
    public static string Normalize(string? configured) => string.Equals(configured?.Trim(), Pcm, StringComparison.OrdinalIgnoreCase) ? Pcm : Mp3;

    private static bool IsMp3(string format) => string.Equals(format, Mp3, StringComparison.OrdinalIgnoreCase);
}
