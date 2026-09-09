using Microsoft.AspNetCore.Authentication;

namespace PoFightJudge.Api.Features.Auth;

public sealed class FakeAuthOptions : AuthenticationSchemeOptions
{
    public const string DefaultScheme = "FakeAuth";
    public const string UserHeader = "X-Fake-User";
    public const string RolesHeader = "X-Fake-Roles";
}

/// <summary>Decided once at startup: which auth surfaces does this environment expose?</summary>
/// <param name="UseFake">Are the Development/Test credentials (FakeAuth header + guest cookie) in play?</param>
/// <param name="DevEntraEnabled">Has Development additionally registered the BFF cookie scheme against the dev Entra registration?</param>
public sealed record AuthMode(bool UseFake, bool DevEntraEnabled = false);
