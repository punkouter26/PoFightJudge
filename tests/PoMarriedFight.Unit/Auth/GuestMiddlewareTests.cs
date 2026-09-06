using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using PoMarriedFight.Api.Features.Auth;
using PoMarriedFight.Shared.Configuration;

namespace PoMarriedFight.Unit.Auth;

public class GuestMiddlewareTests
{
    private static IHostEnvironment Env(string name)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(name);
        return env;
    }

    private static IConfiguration Allow(bool allow) =>
        new ConfigurationBuilder().AddInMemoryCollection([new(ConfigKeys.Auth.AllowFakeAuth, allow ? "true" : "false")]).Build();

    private static DefaultHttpContext ContextWithCookie(string cookie)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.Cookie = cookie;
        return ctx;
    }

    /// <summary>Guest sign-in is the dev door into a real session, so what opens it is worth pinning exactly.</summary>
    [Fact]
    public async Task A_guest_session_needs_a_credible_cookie_or_header_and_nothing_less()
    {
        // Development cookie sets guest principal
        {
            var id = GuestMiddleware.NewGuestId();
            var ctx = ContextWithCookie($"{GuestMiddleware.CookieName}={id}");
            var sut = new GuestMiddleware(_ => Task.CompletedTask);

            await sut.InvokeAsync(ctx);

            ctx.User.Identity!.IsAuthenticated.Should().BeTrue();
            ctx.User.Identity.Name.Should().Be(id);
            id.Should().StartWith("GUEST-").And.HaveLength(38);
            ctx.User.Identity.AuthenticationType.Should().Be(GuestMiddleware.AuthenticationType);
        }

        // Header alone creates a fresh guest
        {
            var ctx = new DefaultHttpContext();
            ctx.Request.Headers[GuestMiddleware.HeaderName] = "true";
            var sut = new GuestMiddleware(_ => Task.CompletedTask);

            await sut.InvokeAsync(ctx);

            ctx.User.Identity!.Name.Should().StartWith("GUEST-");
        }

        // No cookie, no header: stays anonymous
        {
            var ctx = new DefaultHttpContext();
            var sut = new GuestMiddleware(_ => Task.CompletedTask);

            await sut.InvokeAsync(ctx);

            ctx.User.Identity!.IsAuthenticated.Should().BeFalse();
        }

        // Short or legacy guest ids are rejected
        {
            var ctx = ContextWithCookie($"{GuestMiddleware.CookieName}=GUEST-123456");
            var sut = new GuestMiddleware(_ => Task.CompletedTask);

            await sut.InvokeAsync(ctx);

            ctx.User.Identity!.IsAuthenticated.Should().BeFalse();
            GuestMiddleware.IsCredible("GUEST-" + new string('a', 31)).Should().BeFalse();
            GuestMiddleware.IsCredible("guest-" + new string('a', 32)).Should().BeFalse("the prefix is case-sensitive");
        }
    }

    [Fact]
    public void Fake_auth_needs_a_non_production_environment_and_the_explicit_flag()
    {
        (string Environment, bool Flag, bool Expected)[] cases =
        [
            ("Production", true, false), ("Development", false, false), ("Development", true, true),
            ("Test", true, true), ("Staging", true, false),
        ];

        cases.Select(c => GuestMiddleware.IsEnabledIn(Env(c.Environment), Allow(c.Flag)))
            .Should().Equal(cases.Select(c => c.Expected));
    }
}
