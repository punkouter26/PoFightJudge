using Carter;
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
    }

    private static async Task<IReadOnlyList<FighterDto>> ListAsync(IFighterRepository fighters, CancellationToken ct)
    {
        var roster = await fighters.ListAsync(ct);
        return [.. roster.Select(f => f.ToDto())];
    }

    private static async Task<IResult> GetAsync(FighterId tag, IFighterRepository fighters, CancellationToken ct) =>
        await fighters.GetAsync(tag, ct) is { } fighter ? Results.Ok(fighter.ToDto()) : Results.NotFound();
}
