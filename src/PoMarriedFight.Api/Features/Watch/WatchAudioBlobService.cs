using PoMarriedFight.Api.Features.Storage;
using PoMarriedFight.Api.Features.Voice;
using PoMarriedFight.Shared.Identifiers;

namespace PoMarriedFight.Api.Features.Watch;

/// <summary>
/// Where a WATCH match's spoken lines live: one blob per round under the match's own prefix, so deleting a match is
/// a prefix delete and replaying one is a sequence of point reads.
/// </summary>
public interface IWatchAudioStore
{
    /// <summary>Stores one round's audio and returns the blob name to record on the turn.</summary>
    Task<string> SaveRoundAsync(MatchId matchId, int index, TtsAudio audio, CancellationToken ct = default);

    /// <summary>Reads one round's audio back, or null when it was never stored (a match played without a voice).</summary>
    Task<TtsAudio?> GetRoundAsync(MatchId matchId, int index, string format, CancellationToken ct = default);
}

public sealed class WatchAudioBlobService(IAudioBlobStore blobs) : IWatchAudioStore
{
    public async Task<string> SaveRoundAsync(MatchId matchId, int index, TtsAudio audio, CancellationToken ct = default)
    {
        if (audio.IsEmpty)
        {
            return string.Empty;
        }

        var name = IAudioBlobStore.Round(matchId, index, TtsAudioFormats.Extension(audio.Format));
        using var content = new MemoryStream(Convert.FromBase64String(audio.Base64));
        return await blobs.UploadAsync(name, content, TtsAudioFormats.MimeType(audio.Format), ct);
    }

    public async Task<TtsAudio?> GetRoundAsync(MatchId matchId, int index, string format, CancellationToken ct = default)
    {
        var name = IAudioBlobStore.Round(matchId, index, TtsAudioFormats.Extension(format));
        await using var stream = await blobs.OpenReadAsync(name, ct);
        if (stream is null)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        return new TtsAudio(Convert.ToBase64String(buffer.ToArray()), format);
    }
}
