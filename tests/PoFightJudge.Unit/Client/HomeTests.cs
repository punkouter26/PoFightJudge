using Blazored.LocalStorage;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PoFightJudge.Client.Pages;
using PoFightJudge.Client.Services;
using Radzen;

namespace PoFightJudge.Unit.Client;

/// <summary>
/// Home is the three channels and nothing else. History, the leaderboard and the cast are one tap away in the nav
/// bar, so the page does not repeat them; its title is there for a screen reader, which needs the page to open on one.
/// </summary>
public sealed class HomeTests : BunitContext
{
    private readonly IApiClient _api = Substitute.For<IApiClient>();

    public HomeTests()
    {
        Services.AddRadzenComponents();
        Services.AddSingleton(Substitute.For<ILocalStorageService>());
        Services.AddSingleton(_api);
        Services.AddScoped<GfxInterop>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Home_is_the_three_channels_and_asks_the_api_for_nothing()
    {
        var cut = Render<Home>();

        cut.FindAll("article.channel").Should().HaveCount(3, "CPU, 1P and 2P");
        cut.Find("h1").ClassList.Should().Contain("po-sr-only", "the page still opens on its title, for a screen reader");
        cut.FindAll(".tally, .replays, .standings, .hero-side").Should().BeEmpty("those pages are in the nav bar");
        _api.ReceivedCalls().Should().BeEmpty("nothing on the page is read from the API");
    }
}
