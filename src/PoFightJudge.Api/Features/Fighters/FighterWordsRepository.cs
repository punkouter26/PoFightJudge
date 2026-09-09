using System.Globalization;
using System.Text;
using Azure;
using Azure.Data.Tables;
using PoFightJudge.Api.Common;
using PoFightJudge.Api.Features.Storage;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Api.Features.Fighters;

/// <summary>
/// What one person actually said in one debate, kept as words rather than as a reading of them. The style snapshot
/// stored with their result row says what a debate <em>meant</em>; this says what was in it, so a persona can be
/// written from everything they have ever argued rather than from the last night alone.
/// </summary>
/// <param name="Said">Their own words, in the order they said them, cut at <see cref="MaxChars"/>.</param>
public sealed record SpokenDebate(string Tag, MatchId MatchId, DateTimeOffset At, MatchMode Mode, string Said)
{
    /// <summary>
    /// As much of one debate as is worth keeping. Well under the 64 KB a table property holds, and past it a person
    /// is repeating themselves anyway: the corpus is read for how they argue, not to reconstruct the argument.
    /// </summary>
    public const int MaxChars = 4_000;

    /// <summary>Joins what they said into a row, trimmed at the tail so the opening — where the habits are — survives.</summary>
    public static SpokenDebate From(string tag, MatchId matchId, DateTimeOffset at, MatchMode mode, IEnumerable<string> words)
    {
        ArgumentNullException.ThrowIfNull(words);
        var said = string.Join(' ', words.Where(w => !string.IsNullOrWhiteSpace(w)).Select(w => w.Trim()));
        return new SpokenDebate(
            tag,
            matchId,
            at,
            mode,
            said.Length <= MaxChars ? said : string.Concat(said.AsSpan(0, MaxChars), "…"));
    }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Said);
}

/// <summary>
/// Everything each person has ever said, one row per debate, partitioned by tag so a whole speaking history is a
/// single read. Written by both engines: a person's words count the same whether they were arguing another person
/// or a persona.
/// </summary>
public interface IFighterWordsRepository
{
    /// <summary>Idempotent on (tag, match): re-judging a debate rewrites its row rather than adding a second one.</summary>
    Task SaveAsync(SpokenDebate debate, CancellationToken ct = default);

    /// <summary>Every debate this tag has spoken in, newest first.</summary>
    Task<IReadOnlyList<SpokenDebate>> ListAsync(FighterId tag, CancellationToken ct = default);

    Task DeleteForMatchAsync(MatchId matchId, IEnumerable<string> tags, CancellationToken ct = default);

    /// <summary>Forgets everything one person ever said, for when the fighter itself is deleted.</summary>
    Task DeleteForFighterAsync(FighterId tag, CancellationToken ct = default);
}

public sealed class FighterWordsRepository(TableServiceClient tables) : IFighterWordsRepository
{
    private TableClient Table => tables.GetTableClient(TableNames.FighterWords);

    public Task SaveAsync(SpokenDebate debate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(debate);
        return debate.IsEmpty
            ? Task.CompletedTask
            : StorageBootstrap.WithTableAsync(
                tables,
                TableNames.FighterWords,
                token => Table.UpsertEntityAsync(FighterWordsEntity.From(debate), TableUpdateMode.Replace, token),
                ct);
    }

    public Task<IReadOnlyList<SpokenDebate>> ListAsync(FighterId tag, CancellationToken ct = default) =>
        StorageBootstrap.WithTableAsync<IReadOnlyList<SpokenDebate>>(tables, TableNames.FighterWords, async token =>
        {
            var debates = new List<SpokenDebate>();
            await foreach (var entity in Table.QueryAsync<FighterWordsEntity>(TableClient.CreateQueryFilter($"PartitionKey eq {tag.Value}"), cancellationToken: token))
            {
                debates.Add(entity.ToDto());
            }

            return [.. debates.OrderByDescending(d => d.At)];
        }, ct);

    public Task DeleteForMatchAsync(MatchId matchId, IEnumerable<string> tags, CancellationToken ct = default) =>
        StorageBootstrap.WithTableAsync(tables, TableNames.FighterWords, async token =>
        {
            foreach (var tag in tags.Where(t => !string.IsNullOrEmpty(t)).Distinct(StringComparer.Ordinal))
            {
                await Table.DeleteEntityAsync(tag, matchId.Value, cancellationToken: token);
            }
        }, ct);

    public Task DeleteForFighterAsync(FighterId tag, CancellationToken ct = default) =>
        StorageBootstrap.WithTableAsync(tables, TableNames.FighterWords, async token =>
        {
            var keys = new List<string>();
            await foreach (var entity in Table.QueryAsync<FighterWordsEntity>(TableClient.CreateQueryFilter($"PartitionKey eq {tag.Value}"), select: ["RowKey"], cancellationToken: token))
            {
                keys.Add(entity.RowKey);
            }

            foreach (var batch in keys.Chunk(100))
            {
                await Table.SubmitTransactionAsync(
                    batch.Select(k => new TableTransactionAction(TableTransactionActionType.Delete, new TableEntity(tag.Value, k) { ETag = ETag.All })),
                    token);
            }
        }, ct);
}

/// <summary>PartitionKey = tag, RowKey = match id, so one person's whole speaking history is one partition scan.</summary>
public sealed class FighterWordsEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;

    public string RowKey { get; set; } = string.Empty;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public DateTimeOffset At { get; set; }

    /// <summary>Which engine the debate ran in, so the corpus can say where a voice came from.</summary>
    public string Mode { get; set; } = nameof(MatchMode.Fight);

    public string Said { get; set; } = string.Empty;

    public static FighterWordsEntity From(SpokenDebate debate)
    {
        ArgumentNullException.ThrowIfNull(debate);
        return new FighterWordsEntity
        {
            PartitionKey = debate.Tag,
            RowKey = debate.MatchId.Value,
            At = debate.At,
            Mode = debate.Mode.ToString(),
            Said = debate.Said,
        };
    }

    public SpokenDebate ToDto() => new(
        PartitionKey,
        MatchId.From(RowKey),
        At,
        Enum.TryParse<MatchMode>(Mode, ignoreCase: true, out var mode) ? mode : MatchMode.Fight,
        Said);
}

/// <summary>
/// Everything a person has said, laid out for a model to read: oldest first, so it reads as somebody changing over
/// time, and bounded, so a regular's twentieth fight costs the same to write a persona from as their third.
/// </summary>
/// <param name="Text">The debates themselves, oldest first, or empty when they have never said anything.</param>
/// <param name="Debates">How many debates <paramref name="Text"/> is drawn from, so a prompt can say what it is based on.</param>
public sealed record SpokenCorpus(string Text, int Debates)
{
    /// <summary>The whole budget, across every debate. Roughly fifteen hundred tokens of somebody arguing.</summary>
    public const int MaxChars = 6_000;

    /// <summary>Past this the oldest debates say nothing new about how they argue now.</summary>
    public const int MaxDebates = 12;

    public static SpokenCorpus Empty { get; } = new(string.Empty, 0);

    public bool IsEmpty => Text.Length == 0;

    /// <summary>
    /// The most recent debates that fit the budget, rendered oldest first. Newest wins the room: a debate is only
    /// dropped whole, because half a paragraph of somebody arguing reads as somebody being cut off.
    /// </summary>
    public static SpokenCorpus From(IReadOnlyList<SpokenDebate> newestFirst)
    {
        ArgumentNullException.ThrowIfNull(newestFirst);

        var kept = new List<SpokenDebate>();
        var spent = 0;
        foreach (var debate in newestFirst.Where(d => !d.IsEmpty).Take(MaxDebates))
        {
            var cost = debate.Said.Length + HeadingAllowance;
            if (kept.Count > 0 && spent + cost > MaxChars)
            {
                break;
            }

            kept.Add(debate);
            spent += cost;
        }

        if (kept.Count == 0)
        {
            return Empty;
        }

        kept.Reverse();
        var text = new StringBuilder();
        for (var i = 0; i < kept.Count; i++)
        {
            if (i > 0)
            {
                text.AppendLine().AppendLine();
            }

            var debate = kept[i];
            var against = debate.Mode == MatchMode.Watch ? "against a persona" : "against another person";
            var latest = i == kept.Count - 1 ? ", the one just judged" : string.Empty;
            text.Append(CultureInfo.InvariantCulture, $"[{i + 1}] {debate.At:yyyy-MM-dd}, {against}{latest}:")
                .AppendLine()
                .Append('"')
                .Append(debate.Said)
                .Append('"');
        }

        return new SpokenCorpus(text.ToString(), kept.Count);
    }

    /// <summary>Roughly what the date and the "against whom" line costs, so the budget is not blown by the framing.</summary>
    private const int HeadingAllowance = 48;
}
