using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using PoMarriedFight.Api.Common;
using PoMarriedFight.Shared.Identifiers;

namespace PoMarriedFight.Api.Features.Storage;

/// <summary>Match audio and transcripts in the audio container, one folder per match so deletion cascades by prefix.</summary>
public interface IAudioBlobStore
{
    /// <summary>Uploads (or replaces) a blob and returns its name.</summary>
    Task<string> UploadAsync(string blobName, Stream content, string contentType, CancellationToken ct = default);

    Task<Stream?> OpenReadAsync(string blobName, CancellationToken ct = default);

    /// <summary>Removes every blob under the match's folder (tracks, transcripts, round audio).</summary>
    Task DeleteMatchAudioAsync(MatchId matchId, CancellationToken ct = default);

    static string PlayersTrack(MatchId matchId, string extension = "wav") => $"{matchId.Value}/players.{extension}";

    static string HostTrack(MatchId matchId, string extension = "wav") => $"{matchId.Value}/host.{extension}";

    /// <summary>Word-level transcript produced in the browser. Shares the match prefix so deletion still cascades.</summary>
    static string ClientTranscript(MatchId matchId) => $"{matchId.Value}/transcript.json";

    /// <summary>Transcript built from the host's own live captions, written by the orchestrator as the fight ends.</summary>
    static string LiveTranscript(MatchId matchId) => $"{matchId.Value}/live-transcript.json";

    /// <summary>One WATCH round's synthesized line; the extension records the wire format it was recorded in.</summary>
    static string Round(MatchId matchId, int roundIndex, string extension) => $"{matchId.Value}/{roundIndex}.{extension}";
}

public sealed class AudioBlobStore(BlobServiceClient blobs, StorageContainers containers) : IAudioBlobStore
{
    private readonly BlobContainerClient _container = blobs.GetBlobContainerClient(containers.AudioContainer);

    public Task<string> UploadAsync(string blobName, Stream content, string contentType, CancellationToken ct = default) =>
        StorageBootstrap.WithContainerAsync(_container, async token =>
        {
            await _container.GetBlobClient(blobName).UploadAsync(content, new BlobHttpHeaders { ContentType = contentType }, cancellationToken: token);
            return blobName;
        }, ct);

    public Task<Stream?> OpenReadAsync(string blobName, CancellationToken ct = default) =>
        StorageBootstrap.WithContainerAsync(_container, async token =>
        {
            try
            {
                return await _container.GetBlobClient(blobName).OpenReadAsync(cancellationToken: token);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return (Stream?)null;
            }
        }, ct);

    public Task DeleteMatchAudioAsync(MatchId matchId, CancellationToken ct = default) =>
        StorageBootstrap.WithContainerAsync(_container, async token =>
        {
            await foreach (var item in _container.GetBlobsAsync(BlobTraits.None, BlobStates.None, matchId.Value + "/", token))
            {
                await _container.DeleteBlobIfExistsAsync(item.Name, cancellationToken: token);
            }
        }, ct);
}
