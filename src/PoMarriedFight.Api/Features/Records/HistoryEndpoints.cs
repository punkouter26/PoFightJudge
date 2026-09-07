using System.Security.Claims;
using Carter;
using PoMarriedFight.Api.Features.Auth;
using PoMarriedFight.Api.Features.Storage;
using PoMarriedFight.Shared;
using PoMarriedFight.Shared.Identifiers;
using PoMarriedFight.Shared.Models;

namespace PoMarriedFight.Api.Features.Records;

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
    /// Deleting a debate takes everything it produced with it: the turns, the recording, the transcripts, and both
    /// sides' result rows. A record that outlived the debate it came from would be a record of nothing.
    /// </summary>
    private static async Task<IResult> DeleteAsync(
        MatchId id,
        ClaimsPrincipal user,
        IMatchRepository matches,
        IAudioBlobStore blobs,
        CancellationToken ct)
    {
        var userId = user.UserId();
        if (await matches.GetAsync(userId, id, ct) is null)
        {
            return Results.NotFound();
        }

        await blobs.DeleteMatchAudioAsync(id, ct);
        return await matches.DeleteAsync(userId, id, ct) ? Results.NoContent() : Results.NotFound();
    }
}
