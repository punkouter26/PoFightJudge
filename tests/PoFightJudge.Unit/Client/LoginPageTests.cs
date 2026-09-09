using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PoFightJudge.Client.Pages;
using PoFightJudge.Client.Services;
using PoFightJudge.Shared.Models;
using Radzen;

namespace PoFightJudge.Unit.Client;

public class LoginPageTests : BunitContext
{
    private readonly IApiClient _api = Substitute.For<IApiClient>();

    public LoginPageTests()
    {
        Services.AddRadzenComponents();
        Services.AddScoped<ThemeInterop>();
        Services.AddSingleton(_api);
        // Program.cs loads the gate before the first render; under bunit the component's own fallback load does it.
        Services.AddSingleton<FeatureGate>();
        Services.AddScoped<AuthenticationStateProvider>(_ => new ApiAuthStateProvider(_api));
        var env = Substitute.For<IWebAssemblyHostEnvironment>();
        env.Environment.Returns("Development");
        Services.AddSingleton(env);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Guest_button_appears_only_when_the_dev_guest_flag_is_on()
    {
        _api.GetFeaturesAsync(Arg.Any<CancellationToken>()).Returns(new FeatureFlagsDto(false, true, true, true));

        var cut = Render<Login>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Continue as Guest (Dev)"));
        cut.Markup.Should().NotContain("Sign in with Microsoft", "in Development the guest door is the only door");
    }

    [Fact]
    public void Without_the_flag_the_page_explains_how_to_turn_guest_on_and_offers_a_disabled_microsoft_button()
    {
        _api.GetFeaturesAsync(Arg.Any<CancellationToken>()).Returns(new FeatureFlagsDto(false, false, true, true));

        var cut = Render<Login>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Guest sign-in is off"));
        cut.Markup.Should().Contain("Sign in with Microsoft").And.NotContain("Continue as Guest");
    }

    [Fact]
    public async Task Continue_as_guest_signs_in_and_returns_to_a_safe_url_only()
    {
        _api.GetFeaturesAsync(Arg.Any<CancellationToken>()).Returns(new FeatureFlagsDto(false, true, true, true));
        _api.GuestSignInAsync(Arg.Any<CancellationToken>()).Returns(new AuthMeDto(true, "GUEST-1", "GUEST-1", "DevGuest"));
        var nav = Services.GetRequiredService<BunitNavigationManager>();

        // A crafted absolute return url must not bounce the user off-site.
        nav.NavigateTo("login?returnUrl=https%3A%2F%2Fevil.example%2Fphish");
        var cut = Render<Login>();
        await cut.WaitForAssertionAsync(() => cut.Markup.Should().Contain("Continue as Guest (Dev)"));

        await cut.Find(".rz-button.rz-secondary").ClickAsync(new());

        await _api.Received(1).GuestSignInAsync(Arg.Any<CancellationToken>());
        nav.Uri.Should().Be(nav.BaseUri, "an off-site return url falls back to home");
    }
}
