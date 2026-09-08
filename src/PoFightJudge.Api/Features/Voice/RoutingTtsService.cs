using System.Runtime.CompilerServices;
using PoFightJudge.Api.Features.Profiles;

namespace PoFightJudge.Api.Features.Voice;

/// <summary>
/// How the voice chain is ordered and what wire format it asks for.
/// </summary>
/// <param name="PreferFastVoice">
/// Put the fastest configured provider first. Default on: voice synthesis is the single longest wait in a round, and
/// Azure Speech answers in ~1–2 s where Gemini TTS measures 12–17 s.
/// </param>
/// <param name="WireFormat">The format to request; providers that cannot honour it answer in PCM.</param>
/// <param name="CacheEnabled">Whether to consult the content-addressed cache.</param>
public sealed record TtsRoutingOptions(bool PreferFastVoice, string WireFormat, bool CacheEnabled);

/// <summary>
/// Routes each request down a provider chain and returns the audio in the format it actually came back in. Any
/// provider failure falls through to the next: a voice problem must never break a round, and callers never learn
/// which provider spoke.
/// </summary>
/// <remarks>
/// The order is a deliberate choice, not a ranking of quality. With <c>PreferFastVoice</c> on (the default) the chain
/// is Fish-for-this-persona → Azure → Fish-by-shared-default → the terminal provider (Gemini TTS, or the fake). A
/// voice chosen for THIS character still leads, because the celebrity impression is the point of that persona; the
/// shared default reference id is a convenience, so it queues behind the fast provider. With the flag off, any Fish
/// voice leads. The terminal provider is last because it is both the slowest and the bulkiest payload.
/// </remarks>
public sealed partial class RoutingTtsService(IEnumerable<ITtsProvider> providers, TtsRoutingOptions routing, ITtsCache cache, ILogger<RoutingTtsService> logger) : ITtsService
{
    private readonly IReadOnlyList<ITtsProvider> _providers = [.. providers];

    /// <summary>The providers to try, best first — only those actually usable for this persona.</summary>
    public IReadOnlyList<ITtsProvider> ProviderChain(TtsSettings settings)
    {
        var chain = new List<ITtsProvider>(_providers.Count);
        var fish = _providers.FirstOrDefault(p => string.Equals(p.Name, FishAudioService.ProviderName, StringComparison.Ordinal));
        var azure = _providers.FirstOrDefault(p => string.Equals(p.Name, AzureSpeechService.ProviderName, StringComparison.Ordinal));
        var terminal = _providers.LastOrDefault(p => p != fish && p != azure);
        var fishUsable = fish is not null && Usable(fish, settings);
        var personaVoice = !string.IsNullOrWhiteSpace(settings.FishReferenceId);

        if (routing.PreferFastVoice)
        {
            if (fishUsable && personaVoice)
            {
                chain.Add(fish!);
            }

            if (azure is not null && Usable(azure, settings))
            {
                chain.Add(azure);
            }

            if (fishUsable && !personaVoice)
            {
                chain.Add(fish!);
            }
        }
        else
        {
            if (fishUsable)
            {
                chain.Add(fish!);
            }

            if (azure is not null && Usable(azure, settings))
            {
                chain.Add(azure);
            }
        }

        if (terminal is not null)
        {
            chain.Add(terminal);
        }

        return chain;
    }

    public async Task<TtsAudio> GenerateTtsAsync(string text, TtsSettings settings, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return TtsAudio.None;
        }

        foreach (var provider in ProviderChain(settings))
        {
            if (await TrySpeakAsync(provider, text, settings, ct) is { } spoken && !spoken.IsEmpty)
            {
                return spoken;
            }
        }

        return TtsAudio.None;
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

    /// <summary>The cache key includes the provider, so a fallback to a different voice is a different entry rather than a wrong hit.</summary>
    public static string CacheKey(string text, TtsSettings settings, string provider, string format) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{provider}|{format}|{settings.VoiceName}|{settings.Pitch:F2}|{settings.Speed:F2}|{settings.FishReferenceId}|{text}");

    private static bool Usable(ITtsProvider provider, TtsSettings settings) => provider switch
    {
        FishAudioService fish => fish.CanSpeak(settings),
        AzureSpeechService azure => azure.CanSpeak(settings),
        _ => true,
    };

    /// <summary>Runs one provider through the cache. Null means it failed or said nothing and the chain should move on.</summary>
    private async Task<TtsAudio?> TrySpeakAsync(ITtsProvider provider, string text, TtsSettings settings, CancellationToken ct)
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

    [LoggerMessage(EventId = 3401, Level = LogLevel.Warning, Message = "Voice provider {Provider} failed; falling back to the next one in the chain")]
    private static partial void LogProviderFailed(ILogger logger, string provider, Exception exception);

    [LoggerMessage(EventId = 3402, Level = LogLevel.Warning, Message = "TTS chunk {Index} could not be synthesized; the rest of the line still plays: {Reason}")]
    private static partial void LogChunkFailed(ILogger logger, int index, string reason);
}
