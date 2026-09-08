using PoFightJudge.Api.Features.Profiles;

namespace PoFightJudge.Api.Features.Voice;

/// <summary>One speech provider (Gemini TTS, Fish Audio, Azure Speech, the fake). The routing service chains them.</summary>
public interface ITtsProvider
{
    /// <summary>Short stable name for logs, cache keys and the diag page ("gemini", "fish", "azure", "fake").</summary>
    string Name { get; }

    bool IsFake { get; }

    /// <summary>Synthesizes the whole of <paramref name="text"/> as one utterance in the provider's native format.</summary>
    Task<TtsAudio> SynthesizeAsync(string text, TtsSettings settings, CancellationToken ct = default);
}

/// <summary>
/// Provider-agnostic text-to-speech. Picks a provider from the persona's <see cref="TtsSettings"/> and the configured
/// preference order, and returns the audio together with the format it actually came back in.
/// </summary>
public interface ITtsService
{
    Task<TtsAudio> GenerateTtsAsync(string text, TtsSettings settings, CancellationToken ct = default);

    /// <summary>
    /// Synthesizes sentence-group by sentence-group and yields each chunk as it is ready, in speaking order.
    /// Time-to-first-audio is what a listener experiences: the first clause can start playing while the rest of the
    /// line is still being rendered. Chunks are synthesized with bounded look-ahead but always yielded in order,
    /// because audio played out of order is worse than audio that is late. Every chunk carries its own format — the
    /// chain can fall back mid-line.
    /// </summary>
    IAsyncEnumerable<TtsChunk> GenerateTtsStreamAsync(string text, TtsSettings settings, CancellationToken ct = default);
}

/// <summary>One piece of a streamed utterance: <paramref name="Index"/> is the play order and <paramref name="IsLast"/> lets the client stop waiting without counting.</summary>
public readonly record struct TtsChunk(int Index, TtsAudio Audio, bool IsLast);
