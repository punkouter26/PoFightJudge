using System.Net;
using System.Net.Http.Json;
using PoFightJudge.Api.Features.Auth;
using PoFightJudge.Shared;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.E2EAPI;

[Collection(ApiCollection.Name)]
public class AuthTests(ApiFactory factory)
{
    [Fact]
    public async Task Me_reflects_the_credentials_on_the_request()
    {
        // Fake user header authenticates
        {
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add(FakeAuthOptions.UserHeader, "e2e-user");

            var me = await client.GetFromJsonAsync<AuthMeDto>(ApiRoutes.Auth.Me);

            me!.Authenticated.Should().BeTrue();
            me.UserId.Should().Be("e2e-user");
            me.AuthType.Should().Be(FakeAuthOptions.DefaultScheme);
        }

        // Anonymous without credentials
        {
            using var client = factory.CreateClient();
            var me = await client.GetFromJsonAsync<AuthMeDto>(ApiRoutes.Auth.Me);
            me!.Authenticated.Should().BeFalse();
        }
    }

    [Fact]
    public async Task Guest_cookie_flow_authenticates_until_logout()
    {
        using var client = factory.CreateClient(new() { HandleCookies = true });

        var start = await client.PostAsync(ApiRoutes.Auth.Guest, null);
        start.StatusCode.Should().Be(HttpStatusCode.OK);
        var issued = await start.Content.ReadFromJsonAsync<AuthMeDto>();
        issued!.UserId.Should().StartWith(GuestMiddleware.IdPrefix);

        var me = await client.GetFromJsonAsync<AuthMeDto>(ApiRoutes.Auth.Me);
        me!.Authenticated.Should().BeTrue();
        me.AuthType.Should().Be(GuestMiddleware.AuthenticationType);
        me.UserId.Should().Be(issued.UserId);

        (await client.PostAsync(ApiRoutes.Auth.Logout, null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<AuthMeDto>(ApiRoutes.Auth.Me))!.Authenticated.Should().BeFalse();
    }
}
