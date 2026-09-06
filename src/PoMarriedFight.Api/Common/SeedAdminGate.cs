using System.Security.Claims;
using PoMarriedFight.Api.Features.Auth;
using PoMarriedFight.Shared.Configuration;

namespace PoMarriedFight.Api.Common;

/// <summary>
/// Who may run the seed endpoint. The client hides the button; this is the real check, so a hand-crafted request with
/// an ordinary token cannot rewrite the cast. Qualifies: the <c>Admin</c> role (FakeAuth <c>X-Fake-Roles</c> in
/// Development/Test, an Entra app role in Production) or a real login whose email is on the list
/// (<c>PoMarriedFight:Seed:AdminEmails</c>, defaulting to the repository owner). Guests never qualify. Fails closed.
/// </summary>
public static class SeedAdminGate
{
    public static IReadOnlyList<string> DefaultAdminEmails { get; } = ["punkouter26@gmail.com"];

    public static bool IsSeedAdmin(ClaimsPrincipal user, IConfiguration configuration)
    {
        if (user.Identity?.IsAuthenticated != true || IsGuest(user))
        {
            return false;
        }

        if (user.IsAdmin())
        {
            return true;
        }

        var email = user.FindFirstValue("preferred_username") ?? user.FindFirstValue(ClaimTypes.Email) ?? user.FindFirstValue("email");
        return !string.IsNullOrWhiteSpace(email) && Whitelist(configuration).Contains(email.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>A configured list replaces the default rather than extending it.</summary>
    public static IReadOnlyList<string> Whitelist(IConfiguration configuration) =>
        configuration.GetSection(ConfigKeys.Seed.AdminEmails).Get<string[]>() is { Length: > 0 } configured ? configured : DefaultAdminEmails;

    private static bool IsGuest(ClaimsPrincipal user) =>
        string.Equals(user.Identity?.AuthenticationType, GuestMiddleware.AuthenticationType, StringComparison.Ordinal)
        || string.Equals(user.FindFirstValue("auth_type"), GuestMiddleware.AuthenticationType, StringComparison.Ordinal);
}
