using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using PoFightJudge.Api.Features.Auth;
using PoFightJudge.Shared;

namespace PoFightJudge.Unit.Auth;

/// <summary>
/// Outside Production both doors are open: a request carrying a Microsoft token is checked by Entra, and anything
/// else falls to the guest cookie and the FakeAuth header, exactly as before.
/// </summary>
public class AuthSchemeTests
{
    [Theory]
    [InlineData("/api/matches", "Bearer abc.def.ghi", null, true)]
    [InlineData(ApiRoutes.Hubs.Live, null, "abc.def.ghi", true)]
    [InlineData("/api/matches", null, "abc.def.ghi", false)]
    [InlineData("/api/matches", null, null, false)]
    [InlineData("/api/matches", "Basic dXNlcjpwYXNz", null, false)]
    public void A_microsoft_token_goes_to_entra_and_everything_else_to_the_guest_door(string path, string? authorization, string? accessToken, bool entra)
    {
        var http = new DefaultHttpContext();
        http.Request.Path = path;
        if (authorization is not null)
        {
            http.Request.Headers.Authorization = authorization;
        }

        if (accessToken is not null)
        {
            http.Request.QueryString = new QueryString($"?access_token={accessToken}");
        }

        AuthServiceExtensions.SchemeFor(http).Should().Be(entra ? JwtBearerDefaults.AuthenticationScheme : FakeAuthOptions.DefaultScheme);
    }
}
