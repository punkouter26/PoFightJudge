using PoMarriedFight.Api.Features.Profiles;
using PoMarriedFight.Api.Features.Voice;

namespace PoMarriedFight.Api.Features.Ai.Fakes;

/// <summary>
/// The stand-in voice: a soft tone whose length tracks the text (about 60 ms per word plus a breath), as 24 kHz PCM
/// like Gemini TTS, so the whole playback path — chunking, scheduling, the level meters — runs end to end without a key.
/// The pitch slider moves the tone so two personas are audibly different.
/// </summary>
public sealed class FakeTts : ITtsProvider
{
    public const int MillisecondsPerWord = 60;

    public const int BreathMilliseconds = 150;

    public string Name => "fake";

    public bool IsFake => true;

    public Task<TtsAudio> SynthesizeAsync(string text, TtsSettings settings, CancellationToken ct = default) =>
        Task.FromResult(TtsAudio.Pcm(Convert.ToBase64String(Tone(DurationFor(text), 220 * Math.Clamp(settings.Pitch, 0.5, 2.0)))));

    public static TimeSpan DurationFor(string text)
    {
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        return TimeSpan.FromMilliseconds(BreathMilliseconds + (words * MillisecondsPerWord));
    }

    /// <summary>Mono 16-bit little-endian sine with a short fade in and out so chunk joins do not click.</summary>
    public static byte[] Tone(TimeSpan duration, double frequencyHz)
    {
        var samples = (int)(duration.TotalSeconds * TtsAudioFormats.PcmSampleRate);
        var fade = Math.Min(samples / 10, TtsAudioFormats.PcmSampleRate / 100);
        var bytes = new byte[samples * TtsAudioFormats.PcmBytesPerSample];
        for (var i = 0; i < samples; i++)
        {
            var envelope = i < fade ? i / (double)fade : i > samples - fade ? (samples - i) / (double)fade : 1.0;
            var value = (short)(Math.Sin(2 * Math.PI * frequencyHz * i / TtsAudioFormats.PcmSampleRate) * 0.25 * envelope * short.MaxValue);
            bytes[2 * i] = (byte)(value & 0xFF);
            bytes[(2 * i) + 1] = (byte)((value >> 8) & 0xFF);
        }

        return bytes;
    }
}
