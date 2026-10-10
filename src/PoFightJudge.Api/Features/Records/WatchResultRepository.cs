using Azure.Data.Tables;
using Microsoft.Extensions.Caching.Memory;
using PoFightJudge.Api.Common;
using PoFightJudge.Api.Features.Storage;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Api.Features.Records;

/// <summary>
/// A persona's WATCH results, one row per match per side. Nothing here is a running total: a record is counted from
/// these rows on read, so re-judging a match overwrites its row instead of double-counting, and deleting a match
/// removes the record it created.
/// </summary>
public interface IWatchResultRepository
{
    Task SaveAsync(IEnumerable<WatchResultDto> results, CancellationToken ct = default);

    /// <summary>One persona's results, newest first.</summary>
    Task<IReadOnlyList<WatchResultDto>> ListForAsync(string initials, CancellationToken ct = default);

    /// <summary>Every result, for the leaderboard.</summary>
    Task<IReadOnlyList<WatchResultDto>> ListAllAsync(CancellationToken ct = default);

    Task DeleteForMatchAsync(MatchId matchId, IEnumerable<string> initials, CancellationToken ct = default);
}

/// <remarks>
/// The leaderboard reads every row of every partition. That scan is kept for a short while and thrown away the
/// moment this repository writes or deletes a row, so the board is never behind something this process did.
/// </remarks>
public sealed class WatchResultRepository(TableServiceClient tables, IMemoryCache? cache = null) : IWatchResultRepository
{
    private const string EveryResult = "watch-results:all";

    private static readonly TimeSpan KeptFor = TimeSpan.FromSeconds(30);

    public async Task SaveAsync(IEnumerable<WatchResultDto> results, CancellationToken ct = default)
    {
        await StorageBootstrap.WithTableAsync(tables, TableNames.WatchResults, async token =>
        {
            // The two sides live in different partitions, so this cannot be one transaction; each row is idempotent
            // on (initials, match), which is what makes a retried verdict safe.
            foreach (var result in results)
            {
                await Table.UpsertEntityAsync(WatchResultEntity.From(result), TableUpdateMode.Replace, token);
            }
        }, ct);
        cache?.Remove(EveryResult);
    }

    public Task<IReadOnlyList<WatchResultDto>> ListForAsync(string initials, CancellationToken ct = default) =>
        QueryAsync(TableClient.CreateQueryFilter($"PartitionKey eq {initials}"), ct);

    public async Task<IReadOnlyList<WatchResultDto>> ListAllAsync(CancellationToken ct = default)
    {
        if (cache is null)
        {
            return await QueryAsync(filter: null, ct);
        }

        if (!cache.TryGetValue(EveryResult, out IReadOnlyList<WatchResultDto>? all) || all is null)
        {
            all = await QueryAsync(filter: null, ct);
            cache.Set(EveryResult, all, KeptFor);
        }

        return all;
    }

    public async Task DeleteForMatchAsync(MatchId matchId, IEnumerable<string> initials, CancellationToken ct = default)
    {
        await StorageBootstrap.WithTableAsync(tables, TableNames.WatchResults, async token =>
        {
            foreach (var side in initials.Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.Ordinal))
            {
                await Table.DeleteEntityAsync(side, matchId.Value, cancellationToken: token);
            }
        }, ct);
        cache?.Remove(EveryResult);
    }

    private TableClient Table => tables.GetTableClient(TableNames.WatchResults);

    private Task<IReadOnlyList<WatchResultDto>> QueryAsync(string? filter, CancellationToken ct) =>
        StorageBootstrap.WithTableAsync<IReadOnlyList<WatchResultDto>>(tables, TableNames.WatchResults, async token =>
        {
            var results = new List<WatchResultDto>();
            await foreach (var entity in Table.QueryAsync<WatchResultEntity>(filter, cancellationToken: token))
            {
                results.Add(entity.ToDto());
            }

            return [.. results.OrderByDescending(r => r.At)];
        }, ct);
}
