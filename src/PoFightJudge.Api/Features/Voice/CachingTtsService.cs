using System.Runtime.CompilerServices;
using PoFightJudge.Api.Features.Profiles;

namespace PoFightJudge.Api.Features.Voice;

/// <summary>
/// What wire format the voice asks for, and whether the cache is consulted.
/// </summary>
/// <param name="WireFormat">The format to request; a provider that cannot honour it answers in PCM.</param>
/// <param name="CacheEnabled">Whether to consult the content-addressed cache.</param>
public sealed record TtsRoutingOptions(string WireFormat, bool CacheEnabled);

/// <summary>
/// Speaks a line through the one configured provider — Gemini TTS, or the fake — with the content-addressed cache in
/// front of it, and splits a long line into clauses so the first one can start playing while the rest is rendered.
/// A provider failure returns silence rather than throwing: a voice problem must never break a round.
/// </summary>
public sealed partial class CachingTtsService(ITtsProvider provider, TtsRoutingOptions routing, ITtsCache cache, ILogger<CachingTtsService> logger) : ITtsService
{
    public async Task<TtsAudio> GenerateTtsAsync(string text, TtsSettings settings, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return TtsAudio.None;
        }

        return await SpeakAsync(text, settings, ct) ?? TtsAudio.None;
    }

    public async IAsyncEnumerable<TtsChunk> GenerateTtsStreamAsync(string text, TtsSettings settings, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var chunks = SentenceChunker.Split(text);
        if (chunks.Count == 0)
        {
            yield return new TtsChunk(0, TtsAudio.None, IsLast: true);
            yield break;
        }

        // Every chunk is dispatched immediately and awaited in order: synthesis overlaps, but playback order is never
        // at risk, because audio delivered out of order is worse than audio delivered late. The listener hears the
        // line as soon as its first clause is rendered rather than after the last one.
        var pending = new List<Task<TtsAudio>>(chunks.Count);
        foreach (var chunk in chunks)
        {
            pending.Add(GenerateTtsAsync(chunk, settings, ct));
        }

        for (var i = 0; i < pending.Count; i++)
        {
            TtsAudio audio;
            try
            {
#pragma warning disable VSTHRD003 // The tasks are started by this method a few lines above; awaiting them in order is the point.
                audio = await pending[i];
#pragma warning restore VSTHRD003
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One unsynthesizable clause must not silence the rest of the line.
                LogChunkFailed(logger, i, ex.Message);
                audio = TtsAudio.None;
            }

            yield return new TtsChunk(i, audio, IsLast: i == pending.Count - 1);
        }
    }

    /// <summary>The cache key includes the provider, so the fake and the real voice can never collide in one account.</summary>
    public static string CacheKey(string text, TtsSettings settings, string provider, string format) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{provider}|{format}|{settings.VoiceName}|{settings.Pitch:F2}|{settings.Speed:F2}|{text}");

    /// <summary>Runs the provider through the cache. Null means it failed or said nothing.</summary>
    private async Task<TtsAudio?> SpeakAsync(string text, TtsSettings settings, CancellationToken ct)
    {
        var key = routing.CacheEnabled && cache.IsEnabled ? CacheKey(text, settings, provider.Name, routing.WireFormat) : null;
        if (key is not null && await cache.TryGetAsync(key, ct) is { } cached && !cached.IsEmpty)
        {
            return cached;
        }

        try
        {
            var audio = await provider.SynthesizeAsync(text, settings, ct);
            if (audio.IsEmpty)
            {
                return null;
            }

            if (key is not null)
            {
                await cache.SetAsync(key, audio, ct);
            }

            return audio;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogProviderFailed(logger, provider.Name, ex);
            return null;
        }
    }

    [LoggerMessage(EventId = 3401, Level = LogLevel.Warning, Message = "Voice provider {Provider} failed; the line is played silent")]
    private static partial void LogProviderFailed(ILogger logger, string provider, Exception exception);

    [LoggerMessage(EventId = 3402, Level = LogLevel.Warning, Message = "TTS chunk {Index} could not be synthesized; the rest of the line still plays: {Reason}")]
    private static partial void LogChunkFailed(ILogger logger, int index, string reason);
}
