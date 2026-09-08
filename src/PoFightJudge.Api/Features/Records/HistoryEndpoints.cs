using System.Security.Claims;
using Carter;
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

        matches.MapGet("/", ListAsync).Produces<IReadOnlyList<MatchDto>>();

        matches.MapGet(ApiRoutes.Matches.ByIdSegment, GetAsync)
            .Produces<MatchDto>()
            .Produces(StatusCodes.Status404NotFound);

        matches.MapGet(ApiRoutes.Matches.TurnsSegment, GetTurnsAsync)
            .Produces<IReadOnlyList<TurnDto>>()
            .Produces(StatusCodes.Status404NotFound);

        matches.MapDelete(ApiRoutes.Matches.ByIdSegment, DeleteAsync)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IReadOnlyList<MatchDto>> ListAsync(
        ClaimsPrincipal user,
        IMatchRepository matches,
        MatchMode? mode,
        CancellationToken ct) =>
        await matches.ListAsync(user.UserId(), mode, ct);

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
