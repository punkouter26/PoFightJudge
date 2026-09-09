using PoFightJudge.Api.Features.Profiles;

namespace PoFightJudge.Api.Features.Voice;

/// <summary>The speech provider — Fish Audio, Gemini TTS, or the fake. <see cref="ITtsService"/> caches and chunks in front of it.</summary>
public interface ITtsProvider
{
    /// <summary>Short stable name for logs and cache keys ("fish", "gemini" or "fake").</summary>
    string Name { get; }

    bool IsFake { get; }

    /// <summary>
    /// Whether this provider can speak for this particular persona. Only the cloned-voice provider ever says no —
    /// it needs a voice model id, and a persona without one has to be spoken by whoever comes next in the chain.
    /// </summary>
    bool CanSpeak(TtsSettings settings) => true;

    /// <summary>Synthesizes the whole of <paramref name="text"/> as one utterance in the provider's native format.</summary>
    Task<TtsAudio> SynthesizeAsync(string text, TtsSettings settings, CancellationToken ct = default);
}

/// <summary>
/// Text-to-speech with the cache in front of it. Returns the audio together with the format it actually came back in.
/// </summary>
public interface ITtsService
{
    Task<TtsAudio> GenerateTtsAsync(string text, TtsSettings settings, CancellationToken ct = default);

    /// <summary>
    /// Synthesizes sentence-group by sentence-group and yields each chunk as it is ready, in speaking order.
    /// Time-to-first-audio is what a listener experiences: the first clause can start playing while the rest of the
    /// line is still being rendered. Chunks are synthesized with bounded look-ahead but always yielded in order,
    /// because audio played out of order is worse than audio that is late. Every chunk carries its own format — the
    /// provider can answer in a different format mid-line.
    /// </summary>
    IAsyncEnumerable<TtsChunk> GenerateTtsStreamAsync(string text, TtsSettings settings, CancellationToken ct = default);
}

/// <summary>One piece of a streamed utterance: <paramref name="Index"/> is the play order and <paramref name="IsLast"/> lets the client stop waiting without counting.</summary>
public readonly record struct TtsChunk(int Index, TtsAudio Audio, bool IsLast);
