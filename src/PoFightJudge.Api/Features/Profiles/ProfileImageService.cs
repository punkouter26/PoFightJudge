using System.Buffers.Binary;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using PoFightJudge.Api.Common;
using PoFightJudge.Shared.Identifiers;

namespace PoFightJudge.Api.Features.Profiles;

/// <summary>A stored face: always a PNG this service accepted, served by the anonymous face endpoint.</summary>
public sealed record FaceImage(ReadOnlyMemory<byte> Bytes, string ContentType);

/// <summary>
/// Face images live in the faces blob container, never in the table row (Table properties cap at 64 KB and the list
/// endpoint would otherwise ship every photo). The browser crops and encodes the portrait (<c>PoFace.square</c>); the
/// server decodes nothing and only stores a body whose PNG header says 512 px square.
/// </summary>
public interface IProfileImageService
{
    /// <summary>Validates the upload, stores it, and returns the blob name to keep on the profile.</summary>
    Task<Outcome<string>> StoreFaceAsync(ProfileId id, Stream image, CancellationToken ct = default);

    Task<FaceImage?> GetFaceAsync(ProfileId id, CancellationToken ct = default);

    Task DeleteFaceAsync(ProfileId id, CancellationToken ct = default);
}

public sealed class ProfileImageService(BlobServiceClient blobs, StorageContainers containers) : IProfileImageService
{
    public const int Size = 512;

    /// <summary>Roomy for a 512 px square PNG, which tops out near 1 MB.</summary>
    public const int MaxUploadBytes = 2 * 1024 * 1024;

    public const string ContentType = "image/png";

    private readonly BlobContainerClient _container = blobs.GetBlobContainerClient(containers.FacesContainer);

    /// <summary>The PNG signature and the start of the IHDR chunk; width and height follow, big-endian.</summary>
    private static ReadOnlySpan<byte> PngStart => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52];

    public static string BlobName(ProfileId id) => $"{id.Value}.png";

    /// <summary>
    /// Reads the body and checks it is what the client is meant to send: a PNG of exactly <see cref="Size"/> square.
    /// Fails with a user-facing message for: too many bytes, nothing sent, not a PNG, or the wrong dimensions.
    /// </summary>
    public static async Task<Outcome<byte[]>> ReadPngAsync(Stream image, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        if (!await CopyCappedAsync(image, buffer, ct))
        {
            return Outcome.Failure<byte[]>($"Image is larger than {MaxUploadBytes / (1024 * 1024)} MB.");
        }

        var bytes = buffer.ToArray();
        if (bytes.Length == 0)
        {
            return Outcome.Failure<byte[]>("No image was sent.");
        }

        if (bytes.Length < PngStart.Length + 8 || !bytes.AsSpan(0, PngStart.Length).SequenceEqual(PngStart))
        {
            return Outcome.Failure<byte[]>("Not a PNG image.");
        }

        var width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(PngStart.Length));
        var height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(PngStart.Length + 4));
        return width == Size && height == Size
            ? Outcome.Success(bytes)
            : Outcome.Failure<byte[]>($"The portrait must be {Size}×{Size} pixels.");
    }

    public async Task<Outcome<string>> StoreFaceAsync(ProfileId id, Stream image, CancellationToken ct = default)
    {
        var png = await ReadPngAsync(image, ct);
        if (!png.IsSuccess)
        {
            return Outcome.Failure<string>(png.Error!);
        }

        var name = BlobName(id);
        await StorageBootstrap.WithContainerAsync(_container, async token =>
        {
            using var content = new MemoryStream(png.Value);
            await _container.GetBlobClient(name).UploadAsync(content, new BlobHttpHeaders { ContentType = ContentType }, cancellationToken: token);
        }, ct);
        return Outcome.Success(name);
    }

    public Task<FaceImage?> GetFaceAsync(ProfileId id, CancellationToken ct = default) =>
        StorageBootstrap.WithContainerAsync(_container, async token =>
        {
            try
            {
                var response = await _container.GetBlobClient(BlobName(id)).DownloadContentAsync(token);
                // Defence in depth for the anonymous endpoint: only the type this service stores is ever echoed back.
                var type = string.Equals(response.Value.Details.ContentType, ContentType, StringComparison.Ordinal) ? ContentType : "application/octet-stream";
                return new FaceImage(response.Value.Content.ToMemory(), type);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return (FaceImage?)null;
            }
        }, ct);

    public Task DeleteFaceAsync(ProfileId id, CancellationToken ct = default) =>
        StorageBootstrap.WithContainerAsync(_container, token => _container.DeleteBlobIfExistsAsync(BlobName(id), cancellationToken: token), ct);

    private static async Task<bool> CopyCappedAsync(Stream source, MemoryStream target, CancellationToken ct)
    {
        var chunk = new byte[64 * 1024];
        int read;
        while ((read = await source.ReadAsync(chunk, ct)) > 0)
        {
            if (target.Length + read > MaxUploadBytes)
            {
                return false;
            }

            await target.WriteAsync(chunk.AsMemory(0, read), ct);
        }

        return true;
    }
}
