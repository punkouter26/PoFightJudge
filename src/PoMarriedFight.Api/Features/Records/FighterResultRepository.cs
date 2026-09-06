using Azure.Data.Tables;
using PoMarriedFight.Api.Common;
using PoMarriedFight.Api.Features.Storage;
using PoMarriedFight.Shared.Identifiers;
using PoMarriedFight.Shared.Models;

namespace PoMarriedFight.Api.Features.Records;

/// <summary>
/// A person's results, one row per debate they spoke in, from either mode. Nothing here is a running total: a record
/// and a style profile are computed from these rows on read, so re-analysing a debate overwrites its row instead of
/// counting twice, and deleting a debate takes back what it contributed.
/// </summary>
public interface IFighterResultRepository
{
    Task SaveAsync(IEnumerable<FighterResultDto> results, CancellationToken ct = default);

    /// <summary>One person's results, newest first — the input to their record and style profile.</summary>
    Task<IReadOnlyList<FighterResultDto>> ListForAsync(FighterId tag, CancellationToken ct = default);

    /// <summary>Every result, for the leaderboard.</summary>
    Task<IReadOnlyList<FighterResultDto>> ListAllAsync(CancellationToken ct = default);

    Task DeleteForMatchAsync(MatchId matchId, IEnumerable<string> tags, CancellationToken ct = default);

    /// <summary>Removes everything one person ever recorded, for when the fighter itself is deleted.</summary>
    Task DeleteForFighterAsync(FighterId tag, CancellationToken ct = default);
}

public sealed class FighterResultRepository(TableServiceClient tables) : IFighterResultRepository
{
    private TableClient Table => tables.GetTableClient(TableNames.FighterResults);

    public Task SaveAsync(IEnumerable<FighterResultDto> results, CancellationToken ct = default) =>
        StorageBootstrap.WithTableAsync(tables, TableNames.FighterResults, async token =>
        {
            // Each person is their own partition, so this is not one transaction; every row is idempotent on
            // (tag, match), which is what makes a retried verdict or a re-run analysis safe.
            foreach (var result in results)
            {
                await Table.UpsertEntityAsync(FighterResultEntity.From(result), TableUpdateMode.Replace, token);
            }
        }, ct);

    public Task<IReadOnlyList<FighterResultDto>> ListForAsync(FighterId tag, CancellationToken ct = default) =>
        QueryAsync(TableClient.CreateQueryFilter($"PartitionKey eq {tag.Value}"), ct);

    public Task<IReadOnlyList<FighterResultDto>> ListAllAsync(CancellationToken ct = default) => QueryAsync(filter: null, ct);

    public Task DeleteForMatchAsync(MatchId matchId, IEnumerable<string> tags, CancellationToken ct = default) =>
        StorageBootstrap.WithTableAsync(tables, TableNames.FighterResults, async token =>
        {
            foreach (var tag in tags.Where(t => !string.IsNullOrEmpty(t)).Distinct(StringComparer.Ordinal))
            {
                await Table.DeleteEntityAsync(tag, matchId.Value, cancellationToken: token);
            }
        }, ct);

    public Task DeleteForFighterAsync(FighterId tag, CancellationToken ct = default) =>
        StorageBootstrap.WithTableAsync(tables, TableNames.FighterResults, async token =>
        {
            var keys = new List<string>();
            await foreach (var entity in Table.QueryAsync<FighterResultEntity>(TableClient.CreateQueryFilter($"PartitionKey eq {tag.Value}"), select: ["RowKey"], cancellationToken: token))
            {
                keys.Add(entity.RowKey);
            }

            foreach (var batch in keys.Chunk(100))
            {
                await Table.SubmitTransactionAsync(
                    batch.Select(k => new TableTransactionAction(TableTransactionActionType.Delete, new TableEntity(tag.Value, k) { ETag = Azure.ETag.All })),
                    token);
            }
        }, ct);

    private Task<IReadOnlyList<FighterResultDto>> QueryAsync(string? filter, CancellationToken ct) =>
        StorageBootstrap.WithTableAsync<IReadOnlyList<FighterResultDto>>(tables, TableNames.FighterResults, async token =>
        {
            var results = new List<FighterResultDto>();
            await foreach (var entity in Table.QueryAsync<FighterResultEntity>(filter, cancellationToken: token))
            {
                results.Add(entity.ToDto());
            }

            return [.. results.OrderByDescending(r => r.At)];
        }, ct);
}
