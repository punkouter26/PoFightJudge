using System.Security.Claims;
using Carter;
using PoFightJudge.Api.Features.Auth;
using PoFightJudge.Api.Features.Fighters;
using PoFightJudge.Shared;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Api.Features.Records;

/// <summary>
/// One board: the people this account has argued with and the cast that argues on its own, ranked together by the
/// same rules. Both are computed from result rows, so a deleted debate leaves the board immediately. Everybody starts
/// at the same Elo, so the two ratings sit on one scale.
/// </summary>
public sealed class LeaderboardEndpoints : ICarterModule
{
    /// <summary>Below this a win rate is noise. One win from one debate is not the top of anything.</summary>
    public const int MinimumMatches = 2;

    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet(ApiRoutes.Leaderboard.Url, TogetherAsync).RequireAuthorization().WithTags("Records").Produces<IReadOnlyList<LeaderboardRowDto>>();

    private static async Task<IReadOnlyList<LeaderboardRowDto>> TogetherAsync(
        ClaimsPrincipal user,
        IWatchResultRepository watches,
        IFighterResultRepository fights,
        IFighterRepository fighters,
        CancellationToken ct)
    {
        // People: the ones this account has argued with, not everybody who has ever used the app.
        var mine = await fights.ListAllAsync(user.UserId(), ct);
        var roster = (await fighters.ListAsync(ct)).ToDictionary(f => f.Tag, f => f.DisplayName, StringComparer.OrdinalIgnoreCase);
        var people = Rows(mine.Select(r => (r.Tag, r.Won, r.Score)), EloRatings.Compute(mine), tag => roster.GetValueOrDefault(tag, tag));

        var all = await watches.ListAllAsync(ct);
        var cast = Rows(all.Select(r => (r.Initials, r.Won, r.Score)), EloRatings.Compute(all), initials => initials);

        return Together(people, cast);
    }

    private static List<LeaderboardRowDto> Rows(IEnumerable<(string Id, bool Won, int Score)> rows, IReadOnlyDictionary<string, int> ratings, Func<string, string> name) =>
    [
        .. rows.GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase).Select(g => new LeaderboardRowDto(
            g.Key,
            name(g.Key),
            g.Count(),
            g.Count(r => r.Won),
            Math.Round((double)g.Count(r => r.Won) / g.Count(), 3),
            Math.Round(g.Average(r => (double)r.Score), 1))
        {
            Rating = ratings.GetValueOrDefault(g.Key, EloRatings.Start),
        }),
    ];

    /// <summary>
    /// The two lists as one. A person whose persona has also been argued is that person, so their persona's row is
    /// dropped before the cut-off. Ranked by rating, because it knows who they beat; then win rate, the average score
    /// and how many they have argued, so a long record outranks the same numbers from two nights.
    /// </summary>
    public static IReadOnlyList<LeaderboardRowDto> Together(IReadOnlyList<LeaderboardRowDto> people, IReadOnlyList<LeaderboardRowDto> cast)
    {
        var tags = people.Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return
        [
            .. people.Select(p => p with { IsPerson = true })
                .Concat(cast.Where(c => !tags.Contains(c.Id)).Select(c => c with { IsPerson = false }))
                .Where(r => r.Matches >= MinimumMatches)
                .Select(r => r with { WinRate = r.Matches == 0 ? 0 : Math.Round((double)r.Wins / r.Matches, 3) })
                .OrderByDescending(r => r.Rating)
                .ThenByDescending(r => r.WinRate)
                .ThenByDescending(r => r.AverageScore)
                .ThenByDescending(r => r.Matches)
                .ThenBy(r => r.Id, StringComparer.Ordinal),
        ];
    }
}
