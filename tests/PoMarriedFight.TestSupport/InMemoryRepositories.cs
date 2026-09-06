using System.Collections.Concurrent;
using PoMarriedFight.Api.Common;
using PoMarriedFight.Api.Features.Fighters;
using PoMarriedFight.Api.Features.Profiles;
using PoMarriedFight.Api.Features.Records;
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

/// <summary>The roster, in memory. Ensure is idempotent and stamps the last-seen time, exactly as the table one does.</summary>
public sealed class InMemoryFighterRepository : IFighterRepository
{
    private readonly ConcurrentDictionary<string, Fighter> _rows = new(StringComparer.Ordinal);

    public Task<Fighter> EnsureAsync(FighterId id, DateTimeOffset now, CancellationToken ct = default)
    {
        var fighter = _rows.GetOrAdd(id.Value, _ => Fighter.Create(id, now));
        fighter.Seen(now);
        return Task.FromResult(fighter);
    }

    public Task<Fighter?> GetAsync(FighterId id, CancellationToken ct = default) => Task.FromResult(_rows.GetValueOrDefault(id.Value));

    public Task<IReadOnlyList<Fighter>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Fighter>>([.. _rows.Values.OrderByDescending(f => f.LastSeenAt)]);

    public Task<Fighter?> RenameAsync(FighterId id, string? displayName, CancellationToken ct = default)
    {
        if (!_rows.TryGetValue(id.Value, out var fighter))
        {
            return Task.FromResult<Fighter?>(null);
        }

        fighter.Rename(displayName);
        return Task.FromResult<Fighter?>(fighter);
    }

    public Task DeleteAsync(FighterId id, CancellationToken ct = default)
    {
        _rows.TryRemove(id.Value, out _);
        return Task.CompletedTask;
    }
}

/// <summary>A person's results, in memory, keyed the way the table keys them so re-analysing replaces rather than doubles.</summary>
public sealed class InMemoryFighterResultRepository : IFighterResultRepository
{
    private readonly ConcurrentDictionary<(string Tag, string Match), FighterResultDto> _rows = new();

    public Task SaveAsync(IEnumerable<FighterResultDto> results, CancellationToken ct = default)
    {
        foreach (var result in results)
        {
            _rows[(result.Tag, result.MatchId.Value)] = result;
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<FighterResultDto>> ListForAsync(FighterId tag, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<FighterResultDto>>([.. _rows.Values.Where(r => string.Equals(r.Tag, tag.Value, StringComparison.Ordinal)).OrderByDescending(r => r.At)]);

    public Task<IReadOnlyList<FighterResultDto>> ListAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<FighterResultDto>>([.. _rows.Values.OrderByDescending(r => r.At)]);

    public Task DeleteForMatchAsync(MatchId matchId, IEnumerable<string> tags, CancellationToken ct = default)
    {
        foreach (var tag in tags)
        {
            _rows.TryRemove((tag, matchId.Value), out _);
        }

        return Task.CompletedTask;
    }

    public Task DeleteForFighterAsync(FighterId tag, CancellationToken ct = default)
    {
        foreach (var key in _rows.Keys.Where(k => string.Equals(k.Tag, tag.Value, StringComparison.Ordinal)).ToList())
        {
            _rows.TryRemove(key, out _);
        }

        return Task.CompletedTask;
    }
}

