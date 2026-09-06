using Azure.Data.Tables;
using PoMarriedFight.Api.Common;
using PoMarriedFight.Api.Features.Storage;
using PoMarriedFight.Shared.Identifiers;
using PoMarriedFight.Shared.Models;

namespace PoMarriedFight.Api.Features.Records;

/// <summary>
/// Every match, both engines, with its turns and its analysis. Deleting is a cascade by design: a match, its turns,
/// its analysis, its audio and both sides' results go together, because a half-deleted match still shows up on a
/// leaderboard.
/// </summary>
public interface IMatchRepository
{
    Task UpsertAsync(MatchDto match, CancellationToken ct = default);

    Task<MatchDto?> GetAsync(string userId, MatchId id, CancellationToken ct = default);

    /// <summary>One user's history, newest first.</summary>
    Task<IReadOnlyList<MatchDto>> ListAsync(string userId, MatchMode? mode = null, CancellationToken ct = default);

    Task SaveTurnsAsync(MatchId id, IEnumerable<TurnDto> turns, CancellationToken ct = default);

    Task<IReadOnlyList<TurnDto>> GetTurnsAsync(MatchId id, CancellationToken ct = default);

    Task SaveAnalysisAsync(AnalysisRecordDto analysis, CancellationToken ct = default);

    Task<AnalysisRecordDto?> GetAnalysisAsync(MatchId id, CancellationToken ct = default);

    /// <summary>Removes the match and everything hanging off it. Returns false when there was nothing to delete.</summary>
    Task<bool> DeleteAsync(string userId, MatchId id, CancellationToken ct = default);
}

public sealed class MatchRepository(TableServiceClient tables) : IMatchRepository
{
    public Task UpsertAsync(MatchDto match, CancellationToken ct = default) =>
        StorageBootstrap.WithTableAsync(tables, TableNames.Matches, token => Table(TableNames.Matches).UpsertEntityAsync(MatchEntity.From(match), TableUpdateMode.Replace, token), ct);

    public Task<MatchDto?> GetAsync(string userId, MatchId id, CancellationToken ct = default) =>
        StorageBootstrap.WithTableAsync<MatchDto?>(tables, TableNames.Matches, async token =>
        {
            var response = await Table(TableNames.Matches).GetEntityIfExistsAsync<MatchEntity>(userId, id.Value, cancellationToken: token);
            return response.HasValue ? response.Value!.ToDto() : null;
        }, ct);

    public Task<IReadOnlyList<MatchDto>> ListAsync(string userId, MatchMode? mode = null, CancellationToken ct = default) =>
        StorageBootstrap.WithTableAsync<IReadOnlyList<MatchDto>>(tables, TableNames.Matches, async token =>
        {
            var filter = mode is { } m
                ? TableClient.CreateQueryFilter($"PartitionKey eq {userId} and Mode eq {m.ToString()}")
                : TableClient.CreateQueryFilter($"PartitionKey eq {userId}");

            var matches = new List<MatchDto>();
            await foreach (var entity in Table(TableNames.Matches).QueryAsync<MatchEntity>(filter, cancellationToken: token))
            {
                matches.Add(entity.ToDto());
            }

            // Newest first. Sorted here rather than by row key: the id is random, so only the timestamp orders a list.
            return [.. matches.OrderByDescending(m => m.StartedAt)];
        }, ct);

    public Task SaveTurnsAsync(MatchId id, IEnumerable<TurnDto> turns, CancellationToken ct = default) =>
        StorageBootstrap.WithTableAsync(tables, TableNames.Turns, async token =>
        {
            // One transaction per match keeps a partly-written transcript impossible; a match is far below the
            // hundred-entity limit, and the batch is chunked anyway so a longer fight cannot trip it.
            var entities = turns.Select(TurnEntity.From).ToList();
            foreach (var batch in entities.Chunk(100))
            {
                await Table(TableNames.Turns).SubmitTransactionAsync(batch.Select(e => new TableTransactionAction(TableTransactionActionType.UpsertReplace, e)), token);
            }
        }, ct);

    public Task<IReadOnlyList<TurnDto>> GetTurnsAsync(MatchId id, CancellationToken ct = default) =>
        StorageBootstrap.WithTableAsync<IReadOnlyList<TurnDto>>(tables, TableNames.Turns, async token =>
        {
            var turns = new List<TurnDto>();
            await foreach (var entity in Table(TableNames.Turns).QueryAsync<TurnEntity>(TableClient.CreateQueryFilter($"PartitionKey eq {id.Value}"), cancellationToken: token))
            {
                turns.Add(entity.ToDto());
            }

            // The zero-padded row key already orders them; the sort makes that a property of the repository rather
            // than of the key format.
            return [.. turns.OrderBy(t => t.Index)];
        }, ct);

    public Task SaveAnalysisAsync(AnalysisRecordDto analysis, CancellationToken ct = default) =>
        StorageBootstrap.WithTableAsync(tables, TableNames.Analyses, token => Table(TableNames.Analyses).UpsertEntityAsync(AnalysisEntity.From(analysis), TableUpdateMode.Replace, token), ct);

    public Task<AnalysisRecordDto?> GetAnalysisAsync(MatchId id, CancellationToken ct = default) =>
        StorageBootstrap.WithTableAsync<AnalysisRecordDto?>(tables, TableNames.Analyses, async token =>
        {
            var response = await Table(TableNames.Analyses).GetEntityIfExistsAsync<AnalysisEntity>(id.Value, AnalysisEntity.FixedRowKey, cancellationToken: token);
            return response.HasValue ? response.Value!.ToDto() : null;
        }, ct);

    public async Task<bool> DeleteAsync(string userId, MatchId id, CancellationToken ct = default)
    {
        var match = await GetAsync(userId, id, ct);
        if (match is null)
        {
            return false;
        }

        // Children first: a crash midway then leaves an orphaned match row, which the next delete still cleans up,
        // rather than orphaned turns nothing points at.
        await DeleteTurnsAsync(id, ct);
        await StorageBootstrap.WithTableAsync(tables, TableNames.Analyses, token => Table(TableNames.Analyses).DeleteEntityAsync(id.Value, AnalysisEntity.FixedRowKey, cancellationToken: token), ct);
        await DeleteResultsAsync(match, ct);
        await StorageBootstrap.WithTableAsync(tables, TableNames.Matches, token => Table(TableNames.Matches).DeleteEntityAsync(userId, id.Value, cancellationToken: token), ct);
        return true;
    }

    private TableClient Table(string name) => tables.GetTableClient(name);

    private Task DeleteTurnsAsync(MatchId id, CancellationToken ct) =>
        StorageBootstrap.WithTableAsync(tables, TableNames.Turns, async token =>
        {
            var keys = new List<string>();
            await foreach (var entity in Table(TableNames.Turns).QueryAsync<TurnEntity>(TableClient.CreateQueryFilter($"PartitionKey eq {id.Value}"), select: ["RowKey"], cancellationToken: token))
            {
                keys.Add(entity.RowKey);
            }

            foreach (var batch in keys.Chunk(100))
            {
                await Table(TableNames.Turns).SubmitTransactionAsync(
                    batch.Select(k => new TableTransactionAction(TableTransactionActionType.Delete, new TableEntity(id.Value, k) { ETag = Azure.ETag.All })),
                    token);
            }
        }, ct);

    /// <summary>Both sides' result rows, in whichever table this mode writes to.</summary>
    private async Task DeleteResultsAsync(MatchDto match, CancellationToken ct)
    {
        var table = match.Mode == MatchMode.Watch ? TableNames.WatchResults : TableNames.FightResults;
        foreach (var side in new[] { match.Side1, match.Side2 }.Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.Ordinal))
        {
            await StorageBootstrap.WithTableAsync(tables, table, token => Table(table).DeleteEntityAsync(side, match.Id.Value, cancellationToken: token), ct);
        }
    }
}
