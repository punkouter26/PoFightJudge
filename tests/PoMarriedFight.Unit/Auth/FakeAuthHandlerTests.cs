using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using PoMarriedFight.Api.Features.Auth;

namespace PoMarriedFight.Unit.Auth;

public class FakeAuthHandlerTests
{
    private static IHostEnvironment Env(string name)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(name);
        return env;
    }

    private static async Task<FakeAuthHandler> HandlerFor(string environment, HttpContext context)
    {
        var options = Substitute.For<IOptionsMonitor<FakeAuthOptions>>();
        options.Get(Arg.Any<string>()).Returns(new FakeAuthOptions());
        var handler = new FakeAuthHandler(options, NullLoggerFactory.Instance, UrlEncoder.Default, Env(environment));
        await handler.InitializeAsync(new AuthenticationScheme(FakeAuthOptions.DefaultScheme, null, typeof(FakeAuthHandler)), context);
        return handler;
    }

    [Fact]
    public async Task Fake_auth_refuses_to_exist_in_production()
    {
        // Registering in Production throws
        {
            var services = new ServiceCollection();
            var builder = services.AddAuthentication();

            var act = () => builder.AddFakeAuth(Env(Environments.Production));

            act.Should().Throw<InvalidOperationException>().WithMessage("*Production*");
        }

        // Executing in Production throws
        {
            var handler = await HandlerFor(Environments.Production, new DefaultHttpContext());

            var act = () => handler.AuthenticateAsync();

            await act.Should().ThrowAsync<InvalidOperationException>();
        }
    }

    [Fact]
    public async Task The_fake_user_header_is_the_only_thing_that_authenticates()
    {
        // Header yields principal with roles
        {
            var ctx = new DefaultHttpContext();
            ctx.Request.Headers[FakeAuthOptions.UserHeader] = "alice";
            ctx.Request.Headers[FakeAuthOptions.RolesHeader] = "User, Admin";
            var handler = await HandlerFor(Environments.Development, ctx);

            var result = await handler.AuthenticateAsync();

            result.Succeeded.Should().BeTrue();
            result.Principal!.Identity!.Name.Should().Be("alice");
            result.Principal.UserId().Should().Be("alice");
            result.Principal.IsAdmin().Should().BeTrue();
            result.Principal.FindAll(ClaimTypes.Role).Select(c => c.Value).Should().BeEquivalentTo(["User", "Admin"]);
        }

        // No header yields no result
        {
            var handler = await HandlerFor(Environments.Development, new DefaultHttpContext());

            var result = await handler.AuthenticateAsync();

            result.None.Should().BeTrue();
        }
    }

    [Fact]
    public void Claims_helpers_prefer_the_stable_id_and_fall_back_to_entra_claims()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
        anonymous.UserIdOrNull().Should().BeNull();
        anonymous.Invoking(p => p.UserId()).Should().Throw<InvalidOperationException>();

        var entra = new ClaimsPrincipal(new ClaimsIdentity([new Claim("oid", "obj-1"), new Claim("preferred_username", "kev@contoso.com")], "Bearer"));
        entra.UserId().Should().Be("obj-1");
        entra.DisplayName().Should().Be("kev@contoso.com");
        entra.IsAdmin().Should().BeFalse();

        AuthEndpoints.ToDto(entra).Should().Be(new PoMarriedFight.Shared.Models.AuthMeDto(true, "obj-1", "kev@contoso.com", "Bearer"));
        AuthEndpoints.ToDto(anonymous).Authenticated.Should().BeFalse();
    }
}
