using Azure.Data.Tables;
using PoFightJudge.Api.Common;
using PoFightJudge.Api.Features.Storage;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Api.Features.Records;

/// <summary>
/// Every match, both engines, with its turns and its analysis. Deleting is a cascade by design: a match, its turns,
/// its analysis, its audio and both sides' results go together, because a half-deleted match still shows up on a
/// leaderboard — and because deleting a debate must also take back what it contributed to a person's style profile.
/// </summary>
public interface IMatchRepository
{
    Task UpsertAsync(MatchDto match, CancellationToken ct = default);

    Task<MatchDto?> GetAsync(string userId, MatchId id, CancellationToken ct = default);

    /// <summary>One user's history, newest first.</summary>
    Task<IReadOnlyList<MatchDto>> ListAsync(string userId, MatchMode? mode = null, CancellationToken ct = default);

    /// <summary>One page of one user's history, narrowed by <paramref name="query"/> and newest first.</summary>
    Task<MatchPageDto> PageAsync(string userId, MatchQuery query, CancellationToken ct = default);

    Task SaveTurnsAsync(MatchId id, IEnumerable<TurnDto> turns, CancellationToken ct = default);

    Task<IReadOnlyList<TurnDto>> GetTurnsAsync(MatchId id, CancellationToken ct = default);

    Task SaveAnalysisAsync(AnalysisRecordDto analysis, CancellationToken ct = default);

    Task<AnalysisRecordDto?> GetAnalysisAsync(MatchId id, CancellationToken ct = default);

    /// <summary>
    /// Every fight, whoever it belongs to, still marked <see cref="SessionStatus.Analyzing"/> — what a process that
    /// died mid-read left behind. Deliberately not per-user: nobody is signed in when this is asked.
    /// </summary>
    Task<IReadOnlyList<MatchDto>> ListUnfinishedAnalysesAsync(CancellationToken ct = default);

    /// <summary>Attaches a fight's embedding to its analysis row. Best-effort: a fight without one is simply not findable by meaning.</summary>
    Task SaveVectorAsync(MatchId id, IReadOnlyList<float> vector, string describedAs, CancellationToken ct = default);

    /// <summary>The embeddings for one account's fights, for a search to rank. Fights with no vector are left out.</summary>
    Task<IReadOnlyList<FightVectorRow>> ListVectorsAsync(string userId, CancellationToken ct = default);

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

    /// <summary>
    /// The mode and the date range are pushed into the table's own filter, so a narrowed history reads fewer rows.
    /// The text and the ordering cannot be: Table Storage has no contains, and the row key is a random id — only the
    /// timestamp orders this list, and it is not the key. So the partition is still read and then sorted here.
    ///
    /// Paging on top of that is what keeps the answer small. It does not make the read small; the fix for that is a
    /// row key that starts with the timestamp, which is a rewrite of every stored match and not this task.
    /// </summary>
    public Task<MatchPageDto> PageAsync(string userId, MatchQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var sane = query.Sane();

        return StorageBootstrap.WithTableAsync(tables, TableNames.Matches, async token =>
        {
            var matches = new List<MatchDto>();
            await foreach (var entity in Table(TableNames.Matches).QueryAsync<MatchEntity>(Filter(userId, sane), cancellationToken: token))
            {
                matches.Add(entity.ToDto());
            }

            var found = matches.Where(m => Matches(m, sane.Text)).OrderByDescending(m => m.StartedAt).ToList();
            return new MatchPageDto([.. found.Skip(sane.Skip).Take(sane.Take)], found.Count, sane.Skip, sane.Take);
        }, ct);
    }

    /// <summary>Everything the table itself can answer: whose it is, which mode, and when.</summary>
    private static string Filter(string userId, MatchQuery query)
    {
        var clauses = new List<string> { TableClient.CreateQueryFilter($"PartitionKey eq {userId}") };

        if (query.Mode is { } mode)
        {
            clauses.Add(TableClient.CreateQueryFilter($"Mode eq {mode.ToString()}"));
        }

        if (query.From is { } from)
        {
            clauses.Add(TableClient.CreateQueryFilter($"StartedAt ge {from}"));
        }

        if (query.To is { } to)
        {
            clauses.Add(TableClient.CreateQueryFilter($"StartedAt le {to}"));
        }

        return string.Join(" and ", clauses);
    }

    /// <summary>
    /// The four things somebody remembers about an argument: what it was about, who was in it, and who took it.
    /// Case-insensitive because nobody types a tag the way it is stored.
    /// </summary>
    private static bool Matches(MatchDto match, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var needle = text.Trim();
        return match.Topic.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || match.Side1.DisplayName.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || match.Side2.DisplayName.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || match.Side1.Id.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || match.Side2.Id.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || match.Winner.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

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

    /// <summary>
    /// A cross-partition scan, which is what finding these costs: the status is not a key, and the partition is the
    /// account. It runs once per startup over a table with one row per fight ever argued, which on this scale is
    /// cheaper than keeping a second index honest.
    /// </summary>
    public Task<IReadOnlyList<MatchDto>> ListUnfinishedAnalysesAsync(CancellationToken ct = default) =>
        StorageBootstrap.WithTableAsync<IReadOnlyList<MatchDto>>(tables, TableNames.Matches, async token =>
        {
            var filter = TableClient.CreateQueryFilter($"Status eq {nameof(SessionStatus.Analyzing)}");
            var stranded = new List<MatchDto>();
            await foreach (var entity in Table(TableNames.Matches).QueryAsync<MatchEntity>(filter, cancellationToken: token))
            {
                stranded.Add(entity.ToDto());
            }

            // Oldest first, so the fight that has been waiting longest is read first.
            return [.. stranded.OrderBy(m => m.StartedAt)];
        }, ct);

    /// <summary>
    /// Merged onto the analysis row rather than replacing it: the report is written by a different call, and the
    /// two race on a re-analysis. A merge that loses to a concurrent report costs a search result, not a report.
    /// </summary>
    public Task SaveVectorAsync(MatchId id, IReadOnlyList<float> vector, string describedAs, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(vector);
        if (vector.Count == 0)
        {
            return Task.CompletedTask;
        }

        return StorageBootstrap.WithTableAsync(tables, TableNames.Analyses, token => Table(TableNames.Analyses).UpsertEntityAsync(
            new AnalysisEntity
            {
                PartitionKey = id.Value,
                RowKey = AnalysisEntity.FixedRowKey,
                Vector = FightVector.ToBase64(vector),
                VectorText = describedAs,
            },
            TableUpdateMode.Merge,
            token), ct);
    }

    /// <summary>
    /// One read of the account's fights, then one read of the analyses they point at. A vector search on this scale
    /// is a scan and a sort in memory; a hobby history is hundreds of rows, not millions, and an index nobody
    /// maintains is worse than a scan somebody understands.
    /// </summary>
    public async Task<IReadOnlyList<FightVectorRow>> ListVectorsAsync(string userId, CancellationToken ct = default)
    {
        var mine = await ListAsync(userId, MatchMode.Fight, ct);
        if (mine.Count == 0)
        {
            return [];
        }

        return await StorageBootstrap.WithTableAsync<IReadOnlyList<FightVectorRow>>(tables, TableNames.Analyses, async token =>
        {
            var rows = new List<FightVectorRow>(mine.Count);
            foreach (var match in mine)
            {
                var response = await Table(TableNames.Analyses).GetEntityIfExistsAsync<AnalysisEntity>(
                    match.Id.Value, AnalysisEntity.FixedRowKey, cancellationToken: token);
                if (response.HasValue && FightVector.FromBase64(response.Value!.Vector) is { Count: > 0 } vector)
                {
                    rows.Add(new FightVectorRow(match.Id, match.Topic, match.EndedAt ?? match.StartedAt, vector));
                }
            }

            return rows;
        }, ct);
    }

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

    /// <summary>
    /// Each side's result row, from whichever table that side writes to. Chosen per side rather than per mode,
    /// because a watch match can pair an authored persona with a real person: the persona's row lives with the
    /// watch results, the person's with their fighter results.
    /// </summary>
    private async Task DeleteResultsAsync(MatchDto match, CancellationToken ct)
    {
        foreach (var side in new[] { match.Side1, match.Side2 }.Where(s => !string.IsNullOrEmpty(s.Id)).DistinctBy(s => (s.Id, s.Kind)))
        {
            var table = side.IsHuman ? TableNames.FighterResults : TableNames.WatchResults;
            await StorageBootstrap.WithTableAsync(tables, table, token => Table(table).DeleteEntityAsync(side.Id, match.Id.Value, cancellationToken: token), ct);

            // And what they said in it. Words that outlived the debate would keep writing personas from an argument
            // the person asked to have forgotten.
            if (side.IsHuman)
            {
                await StorageBootstrap.WithTableAsync(
                    tables,
                    TableNames.FighterWords,
                    token => Table(TableNames.FighterWords).DeleteEntityAsync(side.Id, match.Id.Value, cancellationToken: token),
                    ct);
            }
        }
    }
}
