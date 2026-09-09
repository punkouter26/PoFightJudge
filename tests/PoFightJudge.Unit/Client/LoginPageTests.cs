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
    public void Dev_with_only_guest_flag_on_shows_the_guest_button_only()
    {
        _api.GetFeaturesAsync(Arg.Any<CancellationToken>()).Returns(new FeatureFlagsDto(false, true, true, DevEntraEnabled: false));

        var cut = Render<Login>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Continue as Guest (Dev)"));
        cut.Markup.Should().NotContain("Sign in with Microsoft", "with DevEntraEnabled off the dev door is the guest door");
    }

    [Fact]
    public void Dev_with_only_dev_entra_flag_on_shows_the_microsoft_button_only()
    {
        _api.GetFeaturesAsync(Arg.Any<CancellationToken>()).Returns(new FeatureFlagsDto(false, false, true, DevEntraEnabled: true));

        var cut = Render<Login>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Sign in with Microsoft"));
        cut.Markup.Should().NotContain("Continue as Guest", "with DevGuestEnabled off the only dev door is the Microsoft door");
    }

    [Fact]
    public void Dev_with_both_flags_on_shows_both_buttons()
    {
        _api.GetFeaturesAsync(Arg.Any<CancellationToken>()).Returns(new FeatureFlagsDto(false, true, true, DevEntraEnabled: true));

        var cut = Render<Login>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Sign in with Microsoft");
            cut.Markup.Should().Contain("Continue as Guest (Dev)");
        });
    }

    [Fact]
    public void Without_either_flag_the_page_explains_how_to_turn_either_door_on()
    {
        _api.GetFeaturesAsync(Arg.Any<CancellationToken>()).Returns(new FeatureFlagsDto(false, false, true, DevEntraEnabled: false));

        var cut = Render<Login>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Both sign-in doors are off"));
        cut.Markup.Should().NotContain("Continue as Guest").And.NotContain("Sign in with Microsoft");
    }

    [Fact]
    public async Task Continue_as_guest_signs_in_and_returns_to_a_safe_url_only()
    {
        _api.GetFeaturesAsync(Arg.Any<CancellationToken>()).Returns(new FeatureFlagsDto(false, true, true, DevEntraEnabled: false));
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

    [Fact]
    public void Dev_microsoft_button_navigates_to_the_dev_oidc_endpoint_with_a_full_page_load()
    {
        _api.GetFeaturesAsync(Arg.Any<CancellationToken>()).Returns(new FeatureFlagsDto(false, true, true, DevEntraEnabled: true));
        var nav = Services.GetRequiredService<BunitNavigationManager>();
        nav.NavigateTo("login?returnUrl=%2Fwatch");
        var cut = Render<Login>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Sign in with Microsoft"));

        // The MS button is the first .rz-button on the page; clicking it must issue a forceLoad navigation to the
        // server's BFF cookie scheme endpoint, NOT a Blazor router push to /authentication/login (the prod path).
        var msButton = cut.FindAll(".rz-button")[0];
        msButton.Click();

        nav.Uri.Should().Contain("/auth/login/dev");
        nav.Uri.Should().Contain("returnUrl");
    }
}
