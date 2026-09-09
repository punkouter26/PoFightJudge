using System.Security.Claims;
using Carter;
using PoFightJudge.Api.Features.Ai;
using PoFightJudge.Api.Features.Auth;
using PoFightJudge.Api.Features.Storage;
using PoFightJudge.Shared;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Api.Features.Records;

/// <summary>
/// Everything the signed-in person has been part of, both modes in one list, and the one way to remove any of it.
/// </summary>
public sealed class HistoryEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var matches = app.MapGroup(ApiRoutes.Matches.Base).RequireAuthorization().WithTags("Records");

        matches.MapGet("/", ListAsync).Produces<MatchPageDto>();

        matches.MapGet(ApiRoutes.Matches.ByIdSegment, GetAsync)
            .Produces<MatchDto>()
            .Produces(StatusCodes.Status404NotFound);

        matches.MapGet(ApiRoutes.Matches.TurnsSegment, GetTurnsAsync)
            .Produces<IReadOnlyList<TurnDto>>()
            .Produces(StatusCodes.Status404NotFound);

        matches.MapGet(ApiRoutes.Matches.SearchSegment, SearchAsync).Produces<FightSearchResponse>();

        matches.MapDelete(ApiRoutes.Matches.ByIdSegment, DeleteAsync)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);
    }

    /// <summary>
    /// One page of history. The parameters are separate rather than a bound object because they arrive as a query
    /// string, and the record that carries them clamps its own numbers — a hand-written take of a million is a
    /// refusal rather than a table scan.
    /// </summary>
    private static async Task<MatchPageDto> ListAsync(
        ClaimsPrincipal user,
        IMatchRepository matches,
        MatchMode? mode,
        string? q,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? skip,
        int? take,
        CancellationToken ct) =>
        await matches.PageAsync(
            user.UserId(),
            new MatchQuery(mode, q, from, to, skip ?? 0, take ?? MatchQuery.DefaultTake),
            ct);

    /// <summary>
    /// The fights closest in meaning to what was typed.
    /// </summary>
    /// <remarks>
    /// The ordinary history filter is a substring match on the topic, because Table Storage has no contains: it
    /// finds "thermostat" only if somebody typed "thermostat". This finds the fight where they argued about the
    /// heating bill for twenty minutes without once using the word.
    ///
    /// One embedding call for the query, then a scan and a sort over the account's own fights. At this scale that
    /// is the right shape — a history is hundreds of rows, and an index nobody maintains is worse than a scan
    /// somebody understands.
    /// </remarks>
    private static async Task<IResult> SearchAsync(
        ClaimsPrincipal user,
        IMatchRepository matches,
        IGeminiEmbedding embedding,
        string? q,
        int? take,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return Results.Ok(FightSearchResponse.Unavailable);
        }

        var query = await embedding.EmbedAsync(q.Trim(), forQuery: true, ct);
        if (query.Count == 0)
        {
            // No embedding service, or it refused. Saying so lets the page fall back to the substring filter rather
            // than showing an empty result that reads as "you have never argued about that".
            return Results.Ok(FightSearchResponse.Unavailable);
        }

        var rows = await matches.ListVectorsAsync(user.UserId(), ct);
        var wanted = Math.Clamp(take ?? MatchQuery.DefaultTake, 1, MatchQuery.MaxTake);
        var found = FightSearch.Rank(rows, query, wanted);

        return Results.Ok(new FightSearchResponse(
            [.. found.Select(f => new FightMatchDto(f.Id, f.Topic, f.At, Math.Round(FightVector.Similarity(f.Vector, query), 4)))],
            Available: true));
    }

    private static async Task<IResult> GetAsync(MatchId id, ClaimsPrincipal user, IMatchRepository matches, CancellationToken ct) =>
        await matches.GetAsync(user.UserId(), id, ct) is { } match ? Results.Ok(match) : Results.NotFound();

    /// <summary>
    /// The lines of one debate, in the order they were said. Ownership is checked against the match first: the turns
    /// table is partitioned by match rather than by user, so it cannot answer "is this mine?" on its own.
    /// </summary>
    private static async Task<IResult> GetTurnsAsync(MatchId id, ClaimsPrincipal user, IMatchRepository matches, CancellationToken ct)
    {
        if (await matches.GetAsync(user.UserId(), id, ct) is null)
        {
            return Results.NotFound();
        }

        var turns = await matches.GetTurnsAsync(id, ct);
        return Results.Ok(turns.OrderBy(t => t.Index).ToList());
    }

    /// <summary>
    /// Deleting a debate takes everything it produced with it: the turns, the recording, the transcripts, and both
    /// sides' result rows. A record that outlived the debate it came from would be a record of nothing.
    /// </summary>
    private static async Task<IResult> DeleteAsync(
        MatchId id,
        ClaimsPrincipal user,
        IMatchRepository matches,
        IAudioBlobStore blobs,
        IShareRepository shares,
        CancellationToken ct)
    {
        var userId = user.UserId();
        if (await matches.GetAsync(userId, id, ct) is not { } match)
        {
            return Results.NotFound();
        }

        // A shared link outliving the argument it points at is the one way a delete could fail to be a delete.
        if (match.ShareToken is { Length: > 0 } token)
        {
            await shares.RevokeAsync(token, ct);
        }

        await blobs.DeleteMatchAudioAsync(id, ct);
        return await matches.DeleteAsync(userId, id, ct) ? Results.NoContent() : Results.NotFound();
    }
}
