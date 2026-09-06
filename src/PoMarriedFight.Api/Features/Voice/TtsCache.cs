namespace PoMarriedFight.Api.Features.Voice;

/// <summary>
/// Content-addressed cache for synthesized speech. The seam lands with the routing chain; the blob-backed
/// implementation and the key derivation arrive in T20, so until then the null cache is what is registered.
/// </summary>
public interface ITtsCache
{
    bool IsEnabled { get; }

    Task<TtsAudio?> TryGetAsync(string key, CancellationToken ct = default);

    Task SetAsync(string key, TtsAudio audio, CancellationToken ct = default);
}

/// <summary>Caching turned off: every request goes to a provider.</summary>
public sealed class NullTtsCache : ITtsCache
{
    public bool IsEnabled => false;

    public Task<TtsAudio?> TryGetAsync(string key, CancellationToken ct = default) => Task.FromResult<TtsAudio?>(null);

    public Task SetAsync(string key, TtsAudio audio, CancellationToken ct = default) => Task.CompletedTask;
}
