using System.Collections.Concurrent;
using PoMarriedFight.Api.Common;
using PoMarriedFight.Api.Features.Profiles;
using PoMarriedFight.Shared.Identifiers;
using PoMarriedFight.Shared.Models;

namespace PoMarriedFight.TestSupport;

/// <summary>
/// The stores the API host runs on under test (E2EAPI, and E2EUI when Docker is absent). They keep the repository
/// contracts honest — same null-for-missing, same replace-on-upsert — without a storage account. Each store grows
/// alongside its real counterpart.
/// </summary>
public sealed class InMemoryProfileRepository : IProfileRepository
{
    private readonly ConcurrentDictionary<string, Profile> _rows = new(StringComparer.Ordinal);

    public Task<Profile?> GetByIdAsync(ProfileId id, CancellationToken ct = default) =>
        Task.FromResult(_rows.GetValueOrDefault(id.Value));

    public Task<IReadOnlyList<Profile>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Profile>>([.. _rows.Values.OrderBy(p => p.Initials, StringComparer.Ordinal)]);

    public Task<IReadOnlyList<Profile>> GetByRoleAsync(ProfileRole role, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Profile>>([.. _rows.Values.Where(p => p.Role == role).OrderBy(p => p.Initials, StringComparer.Ordinal)]);

    public Task UpsertAsync(Profile profile, CancellationToken ct = default)
    {
        _rows[profile.Initials] = profile;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(ProfileId id, CancellationToken ct = default)
    {
        _rows.TryRemove(id.Value, out _);
        return Task.CompletedTask;
    }
}

/// <summary>Runs the real image normalization (so the 400 paths are exercised) and keeps the PNG bytes in memory.</summary>
public sealed class InMemoryProfileImageService : IProfileImageService
{
    private readonly ConcurrentDictionary<string, byte[]> _faces = new(StringComparer.Ordinal);

    public async Task<Outcome<string>> StoreFaceAsync(ProfileId id, Stream image, CancellationToken ct = default)
    {
        var normalized = await ProfileImageService.NormalizeAsync(image, ct);
        if (!normalized.IsSuccess)
        {
            return Outcome.Failure<string>(normalized.Error!);
        }

        var name = ProfileImageService.BlobName(id);
        _faces[name] = normalized.Value;
        return Outcome.Success(name);
    }

    public Task<FaceImage?> GetFaceAsync(ProfileId id, CancellationToken ct = default) =>
        Task.FromResult(_faces.TryGetValue(ProfileImageService.BlobName(id), out var bytes) ? new FaceImage(bytes, ProfileImageService.ContentType) : null);

    public Task DeleteFaceAsync(ProfileId id, CancellationToken ct = default)
    {
        _faces.TryRemove(ProfileImageService.BlobName(id), out _);
        return Task.CompletedTask;
    }
}
