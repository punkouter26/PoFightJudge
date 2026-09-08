using System.Security.Claims;

namespace PoFightJudge.Api.Features.Auth;

public static class ClaimsPrincipalExtensions
{
    /// <summary>The stable user id: NameIdentifier, else Entra <c>oid</c>, else <c>sub</c>; null when unauthenticated.</summary>
    public static string? UserIdOrNull(this ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true
            ? user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("oid") ?? user.FindFirstValue("sub")
            : null;

    public static string UserId(this ClaimsPrincipal user) =>
        user.UserIdOrNull() ?? throw new InvalidOperationException("No user id claim on an authenticated principal.");

    public static string? DisplayName(this ClaimsPrincipal user) =>
        user.Identity?.Name ?? user.FindFirstValue("name") ?? user.FindFirstValue("preferred_username");

    public static bool IsAdmin(this ClaimsPrincipal user) => user.IsInRole("Admin");
}
