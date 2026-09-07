using System.Security.Claims;
using Carter;
using PoMarriedFight.Api.Features.Auth;
using PoMarriedFight.Api.Features.Fighters;
using PoMarriedFight.Shared;
using PoMarriedFight.Shared.Models;

namespace PoMarriedFight.Api.Features.Records;

/// <summary>
/// Two boards, because two different things are being ranked: personas that were written, and people who turned up.
/// Both are computed from result rows, so a deleted debate leaves the board immediately.
/// </summary>
public sealed class LeaderboardEndpoints : ICarterModule
{
    /// <summary>Below this a win rate is noise. One win from one debate is not the top of anything.</summary>
    public const int MinimumMatches = 2;

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var board = app.MapGroup(ApiRoutes.Leaderboard.Base).RequireAuthorization().WithTags("Records");

        board.MapGet(ApiRoutes.Leaderboard.WatchSegment, WatchAsync).Produces<IReadOnlyList<LeaderboardRowDto>>();
        board.MapGet(ApiRoutes.Leaderboard.FightSegment, FightAsync).Produces<IReadOnlyList<LeaderboardRowDto>>();
    }

    /// <summary>The cast, ranked by how often they win the arguments they are written into.</summary>
    private static async Task<IReadOnlyList<LeaderboardRowDto>> WatchAsync(IWatchResultRepository results, CancellationToken ct)
    {
        var all = await results.ListAllAsync(ct);
        return Rank(all
            .GroupBy(r => r.Initials, StringComparer.OrdinalIgnoreCase)
            .Select(g => new LeaderboardRowDto(
                g.Key,
                g.Key,
                g.Count(),
                g.Count(r => r.Won),
                0,
                Math.Round(g.Average(r => (double)r.Score), 1))));
    }

    /// <summary>The people, ranked the same way. A tag is the identity, and the display name is whatever they chose.</summary>
    private static async Task<IReadOnlyList<LeaderboardRowDto>> FightAsync(
        ClaimsPrincipal user,
        IFighterResultRepository results,
        IFighterRepository fighters,
        CancellationToken ct)
    {
        // The board ranks the people this account has argued with, not everybody who has ever used the app.
        var all = await results.ListAllAsync(user.UserId(), ct);
        var roster = (await fighters.ListAsync(ct)).ToDictionary(f => f.Tag, f => f.DisplayName, StringComparer.OrdinalIgnoreCase);

        return Rank(all
            .GroupBy(r => r.Tag, StringComparer.OrdinalIgnoreCase)
            .Select(g => new LeaderboardRowDto(
                g.Key,
                roster.GetValueOrDefault(g.Key, g.Key),
                g.Count(),
                g.Count(r => r.Won),
                0,
                Math.Round(g.Average(r => (double)r.Score), 1))));
    }

    /// <summary>
    /// Win rate first, then the average score, then how many they have argued — so somebody with a long record
    /// outranks somebody with the same rate from two nights.
    /// </summary>
    private static IReadOnlyList<LeaderboardRowDto> Rank(IEnumerable<LeaderboardRowDto> rows) =>
    [
        .. rows
            .Where(r => r.Matches >= MinimumMatches)
            .Select(r => r with { WinRate = r.Matches == 0 ? 0 : Math.Round((double)r.Wins / r.Matches, 3) })
            .OrderByDescending(r => r.WinRate)
            .ThenByDescending(r => r.AverageScore)
            .ThenByDescending(r => r.Matches)
            .ThenBy(r => r.Id, StringComparer.Ordinal),
    ];
}
