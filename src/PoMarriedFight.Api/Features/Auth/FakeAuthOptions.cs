using Microsoft.AspNetCore.Authentication;

namespace PoMarriedFight.Api.Features.Auth;

public sealed class FakeAuthOptions : AuthenticationSchemeOptions
{
    public const string DefaultScheme = "FakeAuth";
    public const string UserHeader = "X-Fake-User";
    public const string RolesHeader = "X-Fake-Roles";
}

/// <summary>Decided once at startup: are the Development/Test credentials (FakeAuth header + guest cookie) in play?</summary>
public sealed record AuthMode(bool UseFake);
