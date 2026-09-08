using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using PoFightJudge.Api.Features.Auth;

namespace PoFightJudge.Api.Features.Watch;

/// <summary>
/// Registers the WATCH engine and the rate limit its AI routes sit behind. The limit is per user, not per process:
/// every call here costs money at a provider, and one runaway client must not spend everyone else's budget or drain
/// the F1 tier's CPU minutes. A guest and a signed-in user are separate buckets because a guest id is per browser.
/// </summary>
public static class WatchServiceExtensions
{
    /// <summary>Named policy applied to every route that reaches a model.</summary>
    public const string AiPolicy = "ai-per-user";

    /// <summary>
    /// Twenty calls a minute. A full six-round match makes about fourteen (six lines, six audio, a verdict), so a
    /// player can finish a match inside the window while a loop cannot run away.
    /// </summary>
    public const int PermitsPerWindow = 20;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public static IServiceCollection AddPoWatch(this IServiceCollection services)
    {
        services.AddScoped<IWatchAudioStore, WatchAudioBlobService>();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(AiPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                // Anonymous callers cannot reach these routes at all (the fallback policy), so a null id only
                // happens in tests; bucketing them together is the safe reading.
                context.User.UserIdOrNull() ?? "anonymous",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = PermitsPerWindow,
                    Window = Window,
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                }));
        });

        return services;
    }
}
