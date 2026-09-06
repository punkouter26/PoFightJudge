using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using PoMarriedFight.Api.Common;

namespace PoMarriedFight.Api.Features.Voice;

/// <summary>
/// Content-addressed cache for synthesized speech. The same line, persona voice, provider and format always maps to
/// the same entry, so a replayed round costs nothing and a re-run of the same fight is instant.
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

/// <summary>
/// Turns the routing service's readable composite key into a blob name. Hashing rather than escaping keeps the name
/// short and legal whatever the dialogue contains, and two-character sharding keeps a long-running container's
/// listing usable.
/// </summary>
public static class TtsCacheKey
{
    public static string BlobName(string key, string format)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return $"{hash[..2]}/{hash}.{TtsAudioFormats.Extension(format)}";
    }
}

/// <summary>
/// Blob-backed cache in the TTS container. A cache miss, a read failure or a write failure is never fatal: the
/// caller simply synthesizes again, so storage trouble costs latency, never a silent round.
/// </summary>
public sealed partial class BlobTtsCache(BlobServiceClient blobs, StorageContainers containers, ILogger<BlobTtsCache> logger) : ITtsCache
{
    private readonly BlobContainerClient _container = blobs.GetBlobContainerClient(containers.TtsCacheContainer);

    public bool IsEnabled => true;

    public async Task<TtsAudio?> TryGetAsync(string key, CancellationToken ct = default)
    {
        // The format is part of the key, so the stored blob's own content type is what the entry was written as.
        foreach (var format in new[] { TtsAudioFormats.Mp3, TtsAudioFormats.Pcm })
        {
            try
            {
                var blob = _container.GetBlobClient(TtsCacheKey.BlobName(key, format));
                var response = await blob.DownloadContentAsync(ct);
                return new TtsAudio(Convert.ToBase64String(response.Value.Content.ToArray()), format);
            }
            catch (RequestFailedException ex) when (ex.Status is 404)
            {
                // Not cached in this format; try the other, then report a miss.
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Anything else is an outage, and the retry policy wraps those in an AggregateException. A cache that
                // cannot be read costs a re-synthesis; it must never take the round down with it.
                LogUnavailable(logger, ex);
                return null;
            }
        }

        return null;
    }

    public async Task SetAsync(string key, TtsAudio audio, CancellationToken ct = default)
    {
        if (audio.IsEmpty)
        {
            return;
        }

        try
        {
            await StorageBootstrap.WithContainerAsync(_container, async token =>
            {
                using var content = new MemoryStream(Convert.FromBase64String(audio.Base64));
                await _container.GetBlobClient(TtsCacheKey.BlobName(key, audio.Format)).UploadAsync(
                    content,
                    new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = TtsAudioFormats.MimeType(audio.Format) } },
                    token);
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failed write only costs the next caller a re-synthesis.
            LogUnavailable(logger, ex);
        }
    }

    [LoggerMessage(EventId = 3501, Level = LogLevel.Warning, Message = "TTS cache unavailable; synthesizing without it")]
    private static partial void LogUnavailable(ILogger logger, Exception exception);
}
