using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using PoMarriedFight.Client.Pages;
using Radzen;

namespace PoMarriedFight.Unit.Client;

public class HomePageTests : BunitContext
{
    public HomePageTests()
    {
        Services.AddRadzenComponents();
        Services.AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2026, 9, 6, 20, 41, 0, TimeSpan.Zero)));
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Home_offers_the_two_channels_and_navigates_to_them()
    {
        var nav = Services.GetRequiredService<BunitNavigationManager>();
        var cut = Render<Home>();

        var channels = cut.FindAll("article.channel");
        channels.Should().HaveCount(2);
        channels[0].GetAttribute("data-channel").Should().Be("1");
        cut.Markup.Should().Contain("Pick the couple").And.Contain("Start the fight");
        cut.Markup.Should().Contain("Sunday", "the eyebrow is the broadcast date line (2026-09-06 is a Sunday)");

        cut.FindAll("article.channel .rz-button")[1].Click();

        nav.Uri.Should().Be(nav.BaseUri + "fight");
    }

    [Fact]
    public void Home_shows_empty_states_until_there_are_matches_and_fighters()
    {
        var cut = Render<Home>();

        cut.FindAll("[role=status]").Should().HaveCount(2);
        cut.Markup.Should().Contain("No matches yet").And.Contain("Nobody on the card");
    }
}
