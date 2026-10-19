using System.Globalization;
using Microsoft.AspNetCore.RateLimiting;
using PoFightJudge.Api.Features.Auth;
using PoFightJudge.Api.Features.Watch;
using PoFightJudge.Shared.Configuration;

namespace PoFightJudge.Api.Features.Diagnostics;

/// <summary>
/// Names the caller for any AI work a request does, and refuses the routes that start AI work once that caller has
/// spent the day's allowance. "Starts AI work" is the routes carrying <see cref="WatchServiceExtensions.AiPolicy"/>,
/// so a new AI route is covered by the same line that rate-limits it.
/// </summary>
public sealed class AiBudgetGate(RequestDelegate next, AiLatencyTracker tracker, IConfiguration config)
{
    private readonly long _limit = config.GetValue<long>(ConfigKeys.Ai.DailyTokenBudget);

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var caller = context.User.UserIdOrNull();
        AiLatencyTracker.Caller = caller;

        if (caller is not null && _limit > 0 && IsAiRoute(context) && tracker.TokensToday(caller) >= _limit)
        {
            context.Response.Headers.RetryAfter = ((int)Math.Ceiling(tracker.UntilTomorrow().TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            await Results.Problem(
                title: "That's today's arguing used up. It starts again at midnight UTC.",
                statusCode: StatusCodes.Status429TooManyRequests).ExecuteAsync(context);
            return;
        }

        await next(context);
    }

    private static bool IsAiRoute(HttpContext context) =>
        string.Equals(
            context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName,
            WatchServiceExtensions.AiPolicy,
            StringComparison.Ordinal);
}
