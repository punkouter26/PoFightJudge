using System.Security.Claims;
using System.Text.Json;
using Carter;
using PoFightJudge.Api.Features.Analysis;
using PoFightJudge.Api.Features.Auth;
using PoFightJudge.Shared;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Api.Features.Records;

/// <summary>
/// Sharing a ruling, and reading one that was shared.
///
/// Two rules shape this. Sharing is always asked for and can always be taken back — nothing becomes readable by being
/// played. And what a reader gets is deliberately thin: the topic, who argued, who took it and the reasons. No audio,
/// no clips, no metrics, no account. A recording of two real people arguing is not something a link should carry.
/// </summary>
public sealed class ShareEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var mine = app.MapGroup(ApiRoutes.Matches.Base).RequireAuthorization().WithTags("Records");

        mine.MapPost(ApiRoutes.Matches.ShareSegment, ShareAsync)
            .Produces<ShareResponse>()
            .Produces(StatusCodes.Status404NotFound);

        mine.MapDelete(ApiRoutes.Matches.ShareSegment, RevokeAsync)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        // Anonymous on purpose: the token is the credential, and a reader has no account here.
        app.MapGet(ApiRoutes.Shares.Base + ApiRoutes.Shares.ByTokenSegment, ReadAsync)
            .AllowAnonymous()
            .WithTags("Records")
            .Produces<SharedMatchDto>()
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ShareAsync(
        MatchId id,
        ClaimsPrincipal user,
        IMatchRepository matches,
        IShareRepository shares,
        CancellationToken ct)
    {
        var userId = user.UserId();
        if (await matches.GetAsync(userId, id, ct) is not { } match)
        {
            return Results.NotFound();
        }

        // Sharing twice is one link, not two: an owner who presses it again wants the address, not a second one.
        var token = await shares.ShareAsync(userId, id, match.ShareToken, ct);
        if (!string.Equals(match.ShareToken, token, StringComparison.Ordinal))
        {
            await matches.UpsertAsync(match with { ShareToken = token }, ct);
        }

        return Results.Ok(new ShareResponse(token, ApiRoutes.Shares.Page(token)));
    }

    private static async Task<IResult> RevokeAsync(
        MatchId id,
        ClaimsPrincipal user,
        IMatchRepository matches,
        IShareRepository shares,
        CancellationToken ct)
    {
        var userId = user.UserId();
        if (await matches.GetAsync(userId, id, ct) is not { } match)
        {
            return Results.NotFound();
        }

        if (match.ShareToken is { Length: > 0 } token)
        {
            await shares.RevokeAsync(token, ct);
            await matches.UpsertAsync(match with { ShareToken = null }, ct);
        }

        return Results.NoContent();
    }

    /// <summary>
    /// The reader's view. The token is resolved to a match and the match is read as its owner, because ownership is
    /// what the storage is partitioned by — the token stands in for the account, and for nothing else.
    /// </summary>
    private static async Task<IResult> ReadAsync(
        string token,
        IMatchRepository matches,
        IShareRepository shares,
        CancellationToken ct)
    {
        if (await shares.ResolveAsync(token, ct) is not { } target)
        {
            return Results.NotFound();
        }

        if (await matches.GetAsync(target.UserId, target.MatchId, ct) is not { } match)
        {
            // The match was deleted but the index row outlived it. Tidy it away rather than leaving a dead link.
            await shares.RevokeAsync(token, ct);
            return Results.NotFound();
        }

        // A revoked-then-reissued token must not read back through an old row: the match is the authority on which
        // token is current, and anything else is a link that was supposed to be dead.
        if (!string.Equals(match.ShareToken, token, StringComparison.Ordinal))
        {
            return Results.NotFound();
        }

        return Results.Ok(new SharedMatchDto(
            match.Mode,
            match.StartedAt,
            match.Topic,
            match.Side1.DisplayName,
            match.Side2.DisplayName,
            match.Winner,
            match.Verdict,
            match.IsFake,
            await ReasonsAsync(match, matches, ct)));
    }

    /// <summary>
    /// The judge's three reasons, when a fight has been read back. Only that much of the report travels: the metrics
    /// are a measurement of two people, and the point of the link is the ruling.
    /// </summary>
    private static async Task<IReadOnlyList<string>> ReasonsAsync(MatchDto match, IMatchRepository matches, CancellationToken ct)
    {
        if (match.Mode != MatchMode.Fight || await matches.GetAnalysisAsync(match.Id, ct) is not { ReportJson: { Length: > 0 } json })
        {
            return [];
        }

        try
        {
            var report = JsonSerializer.Deserialize<AnalysisReportDto>(json, GeminiJudgeClient.JsonOptions);
            return report?.Overall.Reasons ?? [];
        }
        catch (JsonException)
        {
            // An older report shape. The ruling still stands; it just travels without its reasons.
            return [];
        }
    }
}
