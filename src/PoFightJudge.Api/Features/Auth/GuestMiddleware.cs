using System.Security.Cryptography;
using PoFightJudge.Shared.Configuration;

namespace PoFightJudge.Api.Features.Auth;

/// <summary>
/// Development/Test-only guest identity. A <c>dev-guest-id</c> cookie (issued by <c>POST /auth/guest</c>) or an
/// <c>X-Dev-Guest: true</c> header yields an authenticated guest principal. Registered only when
/// <see cref="IsEnabledIn"/> is true (non-production AND the explicit <c>PoFightJudge:Auth:AllowFakeAuth</c> opt-in).
/// </summary>
public sealed class GuestMiddleware(RequestDelegate next)
{
    public const string CookieName = "dev-guest-id";
    public const string HeaderName = "X-Dev-Guest";
    public const string AuthenticationType = "DevGuest";
    public const string IdPrefix = "GUEST-";

    /// <summary>Fake auth needs BOTH a non-production environment AND the explicit opt-in flag. Evaluated once at startup.</summary>
    public static bool IsEnabledIn(IHostEnvironment env, IConfiguration configuration) =>
        (env.IsDevelopment() || env.IsEnvironment("Test")) && configuration.GetValue<bool>(ConfigKeys.Auth.AllowFakeAuth);

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            await next(context);
            return;
        }

        var guestId = context.Request.Cookies[CookieName];
        if (string.IsNullOrWhiteSpace(guestId) && string.Equals(context.Request.Headers[HeaderName], "true", StringComparison.OrdinalIgnoreCase))
        {
            guestId = NewGuestId();
        }

        if (IsCredible(guestId))
        {
            context.User = Principals.Create(guestId, guestId, AuthenticationType, ["User"]);
        }

        await next(context);
    }

    /// <summary>128 bits of randomness — the id is the credential, so it must not be guessable.</summary>
    public static string NewGuestId() => IdPrefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    /// <summary>Only ids this app minted (prefix + 32 hex) are accepted; short or legacy values stay anonymous.</summary>
    public static bool IsCredible([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? guestId) =>
        guestId is not null && guestId.StartsWith(IdPrefix, StringComparison.Ordinal) && guestId.Length == IdPrefix.Length + 32;
}
