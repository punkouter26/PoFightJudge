using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Client.Services;

/// <summary>
/// Development/Test auth state: asks the API who the guest cookie (or FakeAuth header) says we are. Production uses
/// MSAL's provider instead and never registers this one.
/// </summary>
public sealed class ApiAuthStateProvider(IApiClient api) : AuthenticationStateProvider
{
    private static readonly AuthenticationState Anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            return ToState(await api.GetMeAsync());
        }
        catch (HttpRequestException)
        {
            return Anonymous;
        }
    }

    /// <summary>Call after a guest sign-in or logout so AuthorizeView re-evaluates.</summary>
    public void NotifyChanged() => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());

    public static AuthenticationState ToState(AuthMeDto me)
    {
        if (!me.Authenticated)
        {
            return Anonymous;
        }

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, me.UserId ?? string.Empty),
            new Claim(ClaimTypes.Name, me.Name ?? me.UserId ?? "user"),
        ], me.AuthType ?? "api");
        return new AuthenticationState(new ClaimsPrincipal(identity));
    }
}
