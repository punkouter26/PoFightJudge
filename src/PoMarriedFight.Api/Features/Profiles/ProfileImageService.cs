using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using PoMarriedFight.Api.Common;
using PoMarriedFight.Shared.Identifiers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Processing;

namespace PoMarriedFight.Api.Features.Profiles;

/// <summary>A stored face: always the PNG this service wrote, served by the anonymous face endpoint.</summary>
public sealed record FaceImage(ReadOnlyMemory<byte> Bytes, string ContentType);

/// <summary>
/// Face images live in the faces blob container, never in the table row (Table properties cap at 64 KB and the list
/// endpoint would otherwise ship every photo). Every upload is re-encoded, so what is stored is always a 512 px
/// square PNG produced here — no uploaded bytes are ever echoed back from the anonymous endpoint.
/// </summary>
public interface IProfileImageService
{
    /// <summary>Validates and normalizes the upload, stores it, and returns the blob name to keep on the profile.</summary>
    Task<Outcome<string>> StoreFaceAsync(ProfileId id, Stream image, CancellationToken ct = default);

    Task<FaceImage?> GetFaceAsync(ProfileId id, CancellationToken ct = default);

    Task DeleteFaceAsync(ProfileId id, CancellationToken ct = default);
}

public sealed class ProfileImageService(BlobServiceClient blobs, StorageContainers containers) : IProfileImageService
{
    public const int Size = 512;

    public const int MaxUploadBytes = 4 * 1024 * 1024;

    /// <summary>Decompression-bomb guard: the header is read before any pixel is decoded.</summary>
    public const long MaxSourcePixels = 40_000_000;

    public const string ContentType = "image/png";

    private readonly BlobContainerClient _container = blobs.GetBlobContainerClient(containers.FacesContainer);

    public static string BlobName(ProfileId id) => $"{id.Value}.png";

    /// <summary>
    /// Any raster ImageSharp can read (PNG, JPEG, GIF, WebP, BMP, TIFF…) → one 512×512 cover-cropped PNG. Fails with a
    /// user-facing message for: too many bytes, not an image, absurd dimensions, or a body that will not decode.
    /// </summary>
    public static async Task<Outcome<byte[]>> NormalizeAsync(Stream image, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        if (!await CopyCappedAsync(image, buffer, ct))
        {
            return Outcome.Failure<byte[]>($"Image is larger than {MaxUploadBytes / (1024 * 1024)} MB.");
        }

        if (buffer.Length == 0)
        {
            return Outcome.Failure<byte[]>("No image was sent.");
        }

        buffer.Position = 0;
        ImageInfo info;
        try
        {
            info = await Image.IdentifyAsync(buffer, ct);
        }
        catch (ImageFormatException)
        {
            return Outcome.Failure<byte[]>("Not a PNG, JPEG, GIF, WebP or BMP image.");
        }

        if ((long)info.Width * info.Height > MaxSourcePixels)
        {
            return Outcome.Failure<byte[]>("Image dimensions are too large.");
        }

        buffer.Position = 0;
        try
        {
            using var decoded = await Image.LoadAsync(new DecoderOptions { MaxFrames = 1 }, buffer, ct);
            decoded.Mutate(x => x.Resize(new ResizeOptions { Size = new SixLabors.ImageSharp.Size(Size, Size), Mode = ResizeMode.Crop }));
            using var output = new MemoryStream();
            await decoded.SaveAsPngAsync(output, ct);
            return Outcome.Success(output.ToArray());
        }
        catch (ImageFormatException)
        {
            return Outcome.Failure<byte[]>("The image could not be decoded.");
        }
    }

    public async Task<Outcome<string>> StoreFaceAsync(ProfileId id, Stream image, CancellationToken ct = default)
    {
        var normalized = await NormalizeAsync(image, ct);
        if (!normalized.IsSuccess)
        {
            return Outcome.Failure<string>(normalized.Error!);
        }

        var name = BlobName(id);
        await StorageBootstrap.WithContainerAsync(_container, async token =>
        {
            using var content = new MemoryStream(normalized.Value);
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
                // Defence in depth for the anonymous endpoint: only the type this service writes is ever echoed back.
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
