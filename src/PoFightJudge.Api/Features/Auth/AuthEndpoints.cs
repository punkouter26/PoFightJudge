using System.Security.Claims;
using Carter;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using PoFightJudge.Shared;
using PoFightJudge.Shared.Configuration;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Api.Features.Auth;

/// <summary>BFF-facing auth probes at the root: who am I, guest sign-in (Dev/Test only), and logout.</summary>
public sealed class AuthEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var mode = app.ServiceProvider.GetRequiredService<AuthMode>();

        app.MapGet(ApiRoutes.Auth.Me, (HttpContext ctx) => Results.Ok(ToDto(ctx.User))).AllowAnonymous().WithTags("Auth");

        app.MapPost(ApiRoutes.Auth.Logout, (HttpContext ctx) =>
        {
            // Clear the dev guest cookie regardless of the auth scheme in play. A follow-up /auth/me is then
            // anonymous and the SPA's AuthStateProvider signs the user out.
            ctx.Response.Cookies.Delete(GuestMiddleware.CookieName);
            return Results.NoContent();
        }).AllowAnonymous().WithTags("Auth");

        if (!mode.UseFake)
        {
            return;
        }

        app.MapPost(ApiRoutes.Auth.Guest, (HttpContext ctx, TimeProvider clock) =>
        {
            var id = GuestMiddleware.NewGuestId();
            ctx.Response.Cookies.Append(GuestMiddleware.CookieName, id, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Strict,
                Secure = ctx.Request.IsHttps,
                Expires = clock.GetUtcNow().AddHours(8),
            });
            return Results.Ok(new AuthMeDto(true, id, id, GuestMiddleware.AuthenticationType));
        }).AllowAnonymous().WithTags("Auth");
    }

    public static AuthMeDto ToDto(ClaimsPrincipal user) =>
        user.UserIdOrNull() is { } id
            ? new AuthMeDto(true, id, user.DisplayName() ?? id, user.FindFirstValue("auth_type") ?? user.Identity?.AuthenticationType)
            : AuthMeDto.Anonymous;
}

public static class AuthServiceExtensions
{
    /// <summary>The Development/Test default scheme when Entra is configured too: it forwards to one of the two.</summary>
    public const string GuestOrMicrosoft = "GuestOrMicrosoft";

    /// <summary>
    /// Production: Microsoft Entra ID JWT bearer bound to <c>PoFightJudge:AzureAd</c>, also read from the SignalR
    /// <c>access_token</c> query string because browsers cannot set headers on WebSocket upgrades.
    /// Development/Test with the explicit opt-in: the FakeAuth header scheme (+ GuestMiddleware for cookie guests) and,
    /// when an Entra client id is configured, Microsoft sign-in beside it — <see cref="SchemeFor"/> decides per request.
    /// Every endpoint requires authentication unless it opts out with <c>AllowAnonymous</c>.
    /// </summary>
    public static IServiceCollection AddPoAuth(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment, bool useFake)
    {
        services.AddSingleton(new AuthMode(useFake));
        var entra = !useFake || !string.IsNullOrWhiteSpace(configuration[ConfigKeys.AzureAd.ClientId]);
        var auth = services.AddAuthentication(!useFake ? JwtBearerDefaults.AuthenticationScheme : entra ? GuestOrMicrosoft : FakeAuthOptions.DefaultScheme);

        if (useFake)
        {
            auth.AddFakeAuth(environment);
            if (entra)
            {
                auth.AddPolicyScheme(GuestOrMicrosoft, "Guest or Microsoft", o => o.ForwardDefaultSelector = SchemeFor);
            }
        }

        if (entra)
        {
            AddEntra(auth, services, configuration);
        }

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        return services;
    }

    /// <summary>
    /// Outside Production: a request carrying a Microsoft token (the header, or the live hub's <c>access_token</c>) is
    /// Entra's to check; everything else is the guest cookie's or the FakeAuth header's.
    /// </summary>
    public static string SchemeFor(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        var bearer = http.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
        var hubToken = http.Request.Path.StartsWithSegments(ApiRoutes.Hubs.Live, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(http.Request.Query["access_token"]);
        return bearer || hubToken ? JwtBearerDefaults.AuthenticationScheme : FakeAuthOptions.DefaultScheme;
    }

    private static void AddEntra(AuthenticationBuilder auth, IServiceCollection services, IConfiguration configuration)
    {
        auth.AddMicrosoftIdentityWebApi(configuration, ConfigKeys.AzureAd.Section);

        services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
        {
            // Entra tokens carry the audience as either the bare client id or api://{clientId}; accept both
            // (validating only one produced IDX10214 401s on every call in PoMarriedLife).
            var clientId = configuration[ConfigKeys.AzureAd.ClientId];
            if (!string.IsNullOrWhiteSpace(clientId))
            {
                o.TokenValidationParameters.ValidAudiences = [clientId, $"api://{clientId}"];
            }

            var previous = o.Events?.OnMessageReceived;
            o.Events ??= new JwtBearerEvents();
            o.Events.OnMessageReceived = async context =>
            {
                if (previous is not null)
                {
                    await previous(context);
                }

                var token = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments(ApiRoutes.Hubs.Live, StringComparison.OrdinalIgnoreCase))
                {
                    context.Token = token;
                }
            };
        });
    }
}
