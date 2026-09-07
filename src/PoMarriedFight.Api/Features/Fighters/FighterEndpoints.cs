using Carter;
using PoMarriedFight.Api.Features.Records;
using PoMarriedFight.Shared;
using PoMarriedFight.Shared.Identifiers;
using PoMarriedFight.Shared.Models;

namespace PoMarriedFight.Api.Features.Fighters;

/// <summary>
/// The roster: everyone who has ever argued. Reading it is what lets a setup screen suggest a tag somebody already
/// fights under, rather than letting them invent a second one by accident.
/// </summary>
/// <remarks>
/// Fighters are not owned by a user. A tag is a person, and two people who argue on one microphone are usually
/// signed in as one of them; scoping the roster per account would hide half of every couple from themselves.
/// </remarks>
public sealed class FighterEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var fighters = app.MapGroup(ApiRoutes.Fighters.Base).RequireAuthorization().WithTags("Fighters");

        fighters.MapGet("/", ListAsync).Produces<IReadOnlyList<FighterDto>>();

        fighters.MapGet(ApiRoutes.Fighters.ByTagSegment, GetAsync)
            .Produces<FighterDto>()
            .Produces(StatusCodes.Status404NotFound);

        fighters.MapGet(ApiRoutes.Fighters.ProfileSegment, ProfileAsync)
            .Produces<FighterProfileDto>()
            .Produces(StatusCodes.Status404NotFound);

        fighters.MapPut(ApiRoutes.Fighters.ByTagSegment, RenameAsync)
            .Produces<FighterDto>()
            .Produces(StatusCodes.Status404NotFound);

        fighters.MapDelete(ApiRoutes.Fighters.ByTagSegment, DeleteAsync)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);
    }

    /// <summary>Everything one person's page shows: who they are, their record, how they argue, and what they argued.</summary>
    private static async Task<IResult> ProfileAsync(
        FighterId tag,
        IFighterRepository fighters,
        IFighterResultRepository results,
        CancellationToken ct)
    {
        if (await fighters.GetAsync(tag, ct) is not { } fighter)
        {
            return Results.NotFound();
        }

        var rows = await results.ListForAsync(tag, ct);
        var dto = fighter.ToDto();
        return Results.Ok(new FighterProfileDto(
            dto,
            FighterStatsBuilder.Build(dto, rows),
            StyleProfileBuilder.Build(dto.Tag, rows),
            rows));
    }

    /// <summary>
    /// Only the display name can change. The tag is the identity every result is keyed on, so renaming that would
    /// not be a rename — it would be a different person inheriting somebody's record.
    /// </summary>
    private static async Task<IResult> RenameAsync(FighterId tag, RenameFighterRequest? request, IFighterRepository fighters, CancellationToken ct) =>
        await fighters.RenameAsync(tag, request?.DisplayName, ct) is { } renamed ? Results.Ok(renamed.ToDto()) : Results.NotFound();

    /// <summary>
    /// Removes the person and everything on their record. The debates themselves stay: the other side argued in
    /// them too, and they are somebody else's history as much as this person's.
    /// </summary>
    private static async Task<IResult> DeleteAsync(
        FighterId tag,
        IFighterRepository fighters,
        IFighterResultRepository results,
        CancellationToken ct)
    {
        if (await fighters.GetAsync(tag, ct) is null)
        {
            return Results.NotFound();
        }

        foreach (var row in await results.ListForAsync(tag, ct))
        {
            await results.DeleteForMatchAsync(row.MatchId, [tag.Value], ct);
        }

        await fighters.DeleteAsync(tag, ct);
        return Results.NoContent();
    }

    private static async Task<IReadOnlyList<FighterDto>> ListAsync(IFighterRepository fighters, CancellationToken ct)
    {
        var roster = await fighters.ListAsync(ct);
        return [.. roster.Select(f => f.ToDto())];
    }

    private static async Task<IResult> GetAsync(FighterId tag, IFighterRepository fighters, CancellationToken ct) =>
        await fighters.GetAsync(tag, ct) is { } fighter ? Results.Ok(fighter.ToDto()) : Results.NotFound();
}
