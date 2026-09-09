using System.Security.Claims;
using Carter;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using PoFightJudge.Shared;
using PoFightJudge.Shared.Configuration;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Api.Features.Auth;

/// <summary>BFF-facing auth probes at the root: who am I, guest sign-in (Dev/Test only), logout, dev Entra sign-in.</summary>
public sealed class AuthEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var mode = app.ServiceProvider.GetRequiredService<AuthMode>();

        app.MapGet(ApiRoutes.Auth.Me, (HttpContext ctx) => Results.Ok(ToDto(ctx.User))).AllowAnonymous().WithTags("Auth");

        app.MapPost(ApiRoutes.Auth.Logout, (HttpContext ctx) =>
        {
            // Clear the dev guest cookie regardless of the auth scheme in play. The cookie-based OIDC session lives in
            // its own cookie (pofj.dev.auth); the browser drops it as a side-effect of /auth/logout returning 204 because
            // a follow-up /auth/me will be anonymous and the SPA's AuthStateProvider will sign the user out.
            ctx.Response.Cookies.Delete(GuestMiddleware.CookieName);
            return Results.NoContent();
        }).AllowAnonymous().WithTags("Auth");

        // Dev-only: triggers the OIDC code-flow challenge against the dev Entra registration. Returns a redirect to
        // Microsoft's authorize endpoint; the callback lands on /signin-oidc.
        if (mode.UseFake && mode.DevEntraEnabled)
        {
            app.MapGet(ApiRoutes.Auth.DevLogin, (HttpContext ctx) =>
            {
                var properties = new Microsoft.AspNetCore.Authentication.AuthenticationProperties
                {
                    RedirectUri = ctx.Request.Query["returnUrl"].ToString() is { Length: > 1 } u && u.StartsWith('/') ? u : "/",
                };
                return Results.Challenge(properties, new[] { OpenIdConnectDefaults.AuthenticationScheme });
            }).AllowAnonymous().WithTags("Auth");
        }

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
    /// <summary>
    /// Development/Test with the explicit opt-in: FakeAuth header scheme (+ GuestMiddleware for cookie guests).
    /// Otherwise: Microsoft Entra ID JWT bearer bound to <c>PoFightJudge:AzureAd</c>, also read from the SignalR
    /// <c>access_token</c> query string because browsers cannot set headers on WebSocket upgrades.
    /// Every endpoint requires authentication unless it opts out with <c>AllowAnonymous</c>.
    ///
    /// When the environment is Development AND <c>PoFightJudge:Auth:AllowDevEntra</c> is on AND a dev client id is
    /// configured, the BFF cookie scheme is also registered: <c>AddOpenIdConnect</c> runs the code flow against the
    /// dev Entra registration and <c>AddCookie</c> carries the resulting session. The cookie scheme is opt-in: a
    /// contributor without the KV secrets gets the guest door only, the same as before.
    /// </summary>
    public static IServiceCollection AddPoAuth(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment, bool useFake)
    {
        var devEntra = useFake
            && environment.IsDevelopment()
            && configuration.GetValue<bool>(ConfigKeys.Auth.AllowDevEntra)
            && !string.IsNullOrWhiteSpace(configuration[ConfigKeys.Auth.DevClientId]);

        services.AddSingleton(new AuthMode(useFake, devEntra));
        var auth = services.AddAuthentication(useFake ? FakeAuthOptions.DefaultScheme : JwtBearerDefaults.AuthenticationScheme);

        if (useFake)
        {
            auth.AddFakeAuth(environment);
        }
        else
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

        if (devEntra)
        {
            // BFF cookie scheme: server runs the OIDC code flow, the browser only sees our auth cookie. Matches
            // PoLocalCompare / PoWatch / PoRedoImage so the dev sign-in path here looks the same as in those repos.
            auth.AddCookie(BffCookieSchemes.Cookie, o =>
            {
                o.Cookie.Name = "pofj.dev.auth";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;          // OIDC callback is a cross-site redirect back to us
                o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                o.ExpireTimeSpan = TimeSpan.FromHours(8);
                o.SlidingExpiration = true;
                o.Events.OnRedirectToLogin = ctx =>
                {
                    // An API call that hits a 401 must NEVER answer with an HTML redirect; the BFF client reads the
                    // body. Match the convention in PoLocalCompare / PoWatch.
                    if (IsApiRequest(ctx.Request))
                    {
                        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    }

                    ctx.Response.Redirect(ctx.RedirectUri);
                    return Task.CompletedTask;
                };
                o.Events.OnRedirectToAccessDenied = ctx =>
                {
                    if (IsApiRequest(ctx.Request))
                    {
                        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    }

                    ctx.Response.Redirect(ctx.RedirectUri);
                    return Task.CompletedTask;
                };
            });

            var clientId = configuration[ConfigKeys.Auth.DevClientId]!;
            var tenantId = configuration[ConfigKeys.Auth.DevTenantId] ?? "common";
            var authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";

            auth.AddOpenIdConnect(BffCookieSchemes.Oidc, o =>
            {
                o.Authority = authority;
                o.ClientId = clientId;
                o.SignInScheme = BffCookieSchemes.Cookie;
                o.CallbackPath = "/signin-oidc";
                o.SignedOutCallbackPath = "/signout-callback-oidc";
                o.ResponseType = "code";
                o.UsePkce = true;
                o.SaveTokens = false;                              // No downstream API call from this server
                o.GetClaimsFromUserInfoEndpoint = true;
                o.MapInboundClaims = false;                        // Keep 'oid' / 'sub' as-is for ClaimsPrincipalExtensions
                o.Scope.Clear();
                o.Scope.Add("openid");
                o.Scope.Add("profile");
                o.Scope.Add("email");

                // No client secret: this is a public client (SPA-style) and the dev app registration was created
                // without one. Production PoFightJudge still uses MSAL with a separate secret-bearing flow; this dev
                // path is its own thing.
                o.TokenValidationParameters.ValidateIssuer = true;
                o.TokenValidationParameters.IssuerValidator = (issuer, _, _) =>
                {
                    // The dev registration accepts work/school + personal Microsoft accounts, so the issuer may be a
                    // tenant GUID or the MSA consumer (/9188040d-6c67-4c5b-b112-36a304b66dad/v2.0).
                    if (issuer is { } i
                        && i.StartsWith("https://login.microsoftonline.com/", StringComparison.OrdinalIgnoreCase)
                        && i.EndsWith("/v2.0", StringComparison.OrdinalIgnoreCase))
                    {
                        return i;
                    }

                    throw new Microsoft.IdentityModel.Tokens.SecurityTokenInvalidIssuerException(
                        $"Unexpected OIDC issuer: {issuer}");
                };
            });
        }

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        return services;
    }

    private static bool IsApiRequest(HttpRequest request) =>
        request.Path.StartsWithSegments(ApiRoutes.ApiPrefix, StringComparison.OrdinalIgnoreCase)
        || request.Path.StartsWithSegments(ApiRoutes.Hubs.Live, StringComparison.OrdinalIgnoreCase);
}

public static class BffCookieSchemes
{
    public const string Cookie = "BffCookie";
    public const string Oidc = "BffOidc";
}
