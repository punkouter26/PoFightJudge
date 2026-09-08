using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace PoFightJudge.Api.Features.Auth;

/// <summary>
/// Development/Test authentication from the <c>X-Fake-User</c> / <c>X-Fake-Roles</c> headers.
/// Guarded twice: <see cref="FakeAuthExtensions.AddFakeAuth"/> refuses to register it in Production and the handler
/// itself throws if it ever executes there.
/// </summary>
public sealed class FakeAuthHandler(
    IOptionsMonitor<FakeAuthOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IHostEnvironment environment) : AuthenticationHandler<FakeAuthOptions>(options, loggerFactory, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (environment.IsProduction())
        {
            throw new InvalidOperationException("FakeAuthHandler MUST NOT execute in Production.");
        }

        var user = Request.Headers[FakeAuthOptions.UserHeader].ToString();
        if (string.IsNullOrWhiteSpace(user))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var roles = Request.Headers[FakeAuthOptions.RolesHeader].ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var principal = Principals.Create(user, user, FakeAuthOptions.DefaultScheme, roles);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}

public static class FakeAuthExtensions
{
    /// <summary>Registers the FakeAuth scheme. Throws in Production — the scheme exists only for Development and Test.</summary>
    public static AuthenticationBuilder AddFakeAuth(this AuthenticationBuilder builder, IHostEnvironment environment)
    {
        if (environment.IsProduction())
        {
            throw new InvalidOperationException("FakeAuthHandler MUST NOT be registered in Production.");
        }

        return builder.AddScheme<FakeAuthOptions, FakeAuthHandler>(FakeAuthOptions.DefaultScheme, _ => { });
    }
}

internal static class Principals
{
    public static ClaimsPrincipal Create(string userId, string name, string authenticationType, IEnumerable<string> roles)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new("oid", userId),
            new("sub", userId),
            new(ClaimTypes.Name, name),
            new("auth_type", authenticationType),
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType));
    }
}
