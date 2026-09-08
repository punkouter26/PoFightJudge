using Azure.Data.Tables;
using PoFightJudge.Api.Common;
using PoFightJudge.Api.Features.Storage;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Api.Features.Fighters;

/// <summary>
/// The roster of real people. Global, not per user: a tag is the same person whoever started the match, which is
/// what lets two people share a microphone and still each keep their own profile.
/// </summary>
public interface IFighterRepository
{
    /// <summary>
    /// Returns the fighter for this tag, creating it on first sight and stamping the last-seen time either way.
    /// A <paramref name="role"/> is the seat chosen for them this time; null leaves whatever they had.
    /// </summary>
    Task<Fighter> EnsureAsync(FighterId id, DateTimeOffset now, ProfileRole? role, CancellationToken ct = default);

    Task<Fighter?> GetAsync(FighterId id, CancellationToken ct = default);

    /// <summary>The whole roster, most recently seen first.</summary>
    Task<IReadOnlyList<Fighter>> ListAsync(CancellationToken ct = default);

    /// <summary>Changes the display name only. Returns null when the tag has never argued.</summary>
    Task<Fighter?> RenameAsync(FighterId id, string? displayName, CancellationToken ct = default);

    Task DeleteAsync(FighterId id, CancellationToken ct = default);
}

public sealed class FighterRepository(TableServiceClient tables) : IFighterRepository
{
    private TableClient Table => tables.GetTableClient(TableNames.Fighters);

    public Task<Fighter> EnsureAsync(FighterId id, DateTimeOffset now, ProfileRole? role, CancellationToken ct = default) =>
        StorageBootstrap.WithTableAsync(tables, TableNames.Fighters, async token =>
        {
            var existing = await ReadAsync(id, token);
            var fighter = existing ?? Fighter.Create(id, now);
            fighter.Seen(now);
            if (role is { } chosen)
            {
                fighter.ArgueAs(chosen);
            }

            await Table.UpsertEntityAsync(FighterEntity.From(fighter.ToDto()), TableUpdateMode.Replace, token);
            return fighter;
        }, ct);

    public Task<Fighter?> GetAsync(FighterId id, CancellationToken ct = default) =>
        StorageBootstrap.WithTableAsync(tables, TableNames.Fighters, token => ReadAsync(id, token), ct);

    public Task<IReadOnlyList<Fighter>> ListAsync(CancellationToken ct = default) =>
        StorageBootstrap.WithTableAsync<IReadOnlyList<Fighter>>(tables, TableNames.Fighters, async token =>
        {
            var fighters = new List<Fighter>();
            await foreach (var entity in Table.QueryAsync<FighterEntity>(TableClient.CreateQueryFilter($"PartitionKey eq {FighterEntity.Partition}"), cancellationToken: token))
            {
                fighters.Add(Fighter.Rehydrate(entity.ToDto()));
            }

            return [.. fighters.OrderByDescending(f => f.LastSeenAt)];
        }, ct);

    public Task<Fighter?> RenameAsync(FighterId id, string? displayName, CancellationToken ct = default) =>
        StorageBootstrap.WithTableAsync(tables, TableNames.Fighters, async token =>
        {
            var fighter = await ReadAsync(id, token);
            if (fighter is null)
            {
                return null;
            }

            fighter.Rename(displayName);
            await Table.UpsertEntityAsync(FighterEntity.From(fighter.ToDto()), TableUpdateMode.Replace, token);
            return fighter;
        }, ct);

    public Task DeleteAsync(FighterId id, CancellationToken ct = default) =>
        StorageBootstrap.WithTableAsync(tables, TableNames.Fighters, token => Table.DeleteEntityAsync(FighterEntity.Partition, id.Value, cancellationToken: token), ct);

    private async Task<Fighter?> ReadAsync(FighterId id, CancellationToken ct)
    {
        var response = await Table.GetEntityIfExistsAsync<FighterEntity>(FighterEntity.Partition, id.Value, cancellationToken: ct);
        return response.HasValue ? Fighter.Rehydrate(response.Value!.ToDto()) : null;
    }
}
