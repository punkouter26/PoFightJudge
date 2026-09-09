using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PoFightJudge.Client.Components;
using PoFightJudge.Client.Services;
using PoFightJudge.Shared.Models;
using Radzen;

namespace PoFightJudge.Unit.Client;

/// <summary>
/// The banner sits in the layout, so it renders on the sign-in page before anyone has a token. It therefore has to
/// ask through the typed client, whose feature-flag call is anonymous — the first release shipped it on the raw
/// authorized HttpClient and the production token handler threw before the request left the browser.
/// </summary>
public class FakeAiBannerTests : BunitContext
{
    private readonly IApiClient _api = Substitute.For<IApiClient>();

    public FakeAiBannerTests()
    {
        Services.AddRadzenComponents();
        Services.AddSingleton(_api);
        // Program.cs loads the gate before the first render; under bunit the component's own fallback load does it.
        Services.AddSingleton<FeatureGate>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Shows_when_the_api_says_the_ai_is_fake()
    {
        _api.GetFeaturesAsync(Arg.Any<CancellationToken>()).Returns(new FeatureFlagsDto(true, true, false));

        var cut = Render<FakeAiBanner>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Using fake AI"));
    }

    [Fact]
    public void Stays_hidden_when_the_ai_is_real_or_the_api_is_unreachable()
    {
        _api.GetFeaturesAsync(Arg.Any<CancellationToken>()).Returns(new FeatureFlagsDto(false, false, true));
        Render<FakeAiBanner>().Markup.Should().NotContain("Using fake AI");

        _api.GetFeaturesAsync(Arg.Any<CancellationToken>()).Returns<FeatureFlagsDto>(_ => throw new HttpRequestException("down"));
        Render<FakeAiBanner>().Markup.Should().NotContain("Using fake AI");
    }
}
