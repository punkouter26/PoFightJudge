using System.Security.Claims;
using Carter;
using PoMarriedFight.Api.Features.Auth;
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
/// The roster itself is not owned by anybody. A tag is a person, and two people who argue on one microphone are
/// usually signed in as one of them, so scoping the list of tags per account would hide half of every couple from
/// themselves.
///
/// Their record is a different matter. What was argued about, who said the best line and how it was scored come
/// out of somebody's private debates, so every read of the results is filtered to the account that ran them —
/// otherwise anybody could walk the three-letter tag space and read a stranger's arguments back to them.
/// </remarks>
public sealed class FighterEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var fighters = app.MapGroup(ApiRoutes.Fighters.Base).RequireAuthorization().WithTags("Fighters");

        fighters.MapGet("/", ListAsync).Produces<IReadOnlyList<FighterDto>>();

        fighters.MapGet(ApiRoutes.Fighters.RosterSegment, RosterAsync).Produces<IReadOnlyList<FighterStatsDto>>();

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

    /// <summary>
    /// The roster with everybody's record, in one read of the result rows rather than one read per person. It is
    /// what a list of fighters has to say to be worth looking at: a page of bare tags says nothing.
    /// </summary>
    private static async Task<IReadOnlyList<FighterStatsDto>> RosterAsync(
        ClaimsPrincipal user,
        IFighterRepository fighters,
        IFighterResultRepository results,
        CancellationToken ct)
    {
        var roster = await fighters.ListAsync(ct);
        var byTag = (await results.ListAllAsync(user.UserId(), ct))
            .GroupBy(r => r.Tag, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, IReadOnlyList<FighterResultDto> (g) => [.. g], StringComparer.OrdinalIgnoreCase);

        return
        [
            .. roster
                .Select(f => f.ToDto())
                .Select(dto => FighterStatsBuilder.Build(dto, byTag.GetValueOrDefault(dto.Tag, [])))
                .OrderByDescending(s => s.Fights)
                .ThenByDescending(s => s.LastFoughtAt)
                .ThenBy(s => s.Tag, StringComparer.Ordinal),
        ];
    }

    /// <summary>Everything one person's page shows: who they are, their record, how they argue, and what they argued.</summary>
    private static async Task<IResult> ProfileAsync(
        FighterId tag,
        ClaimsPrincipal user,
        IFighterRepository fighters,
        IFighterResultRepository results,
        CancellationToken ct)
    {
        if (await fighters.GetAsync(tag, ct) is not { } fighter)
        {
            return Results.NotFound();
        }

        var rows = await results.ListForAsync(tag, user.UserId(), ct);
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
    /// Forgets this person: everything they have on record with this account. The debates themselves stay — the
    /// other side argued in them too, and they are somebody else's history as much as this person's — and the tag
    /// stays on the roster as long as anybody else still has a record under it.
    /// </summary>
    private static async Task<IResult> DeleteAsync(
        FighterId tag,
        ClaimsPrincipal user,
        IFighterRepository fighters,
        IFighterResultRepository results,
        CancellationToken ct)
    {
        if (await fighters.GetAsync(tag, ct) is null)
        {
            return Results.NotFound();
        }

        foreach (var row in await results.ListForAsync(tag, user.UserId(), ct))
        {
            await results.DeleteForMatchAsync(row.MatchId, [tag.Value], ct);
        }

        if ((await results.ListForAnyoneAsync(tag, ct)).Count == 0)
        {
            await fighters.DeleteAsync(tag, ct);
        }

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
