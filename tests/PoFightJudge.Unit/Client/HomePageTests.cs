using Blazored.LocalStorage;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using PoFightJudge.Client.Components;
using PoFightJudge.Client.Pages;
using PoFightJudge.Client.Services;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;
using Radzen;

namespace PoFightJudge.Unit.Client;

public class HomePageTests : BunitContext
{
    private static readonly DateTimeOffset Night = new(2026, 9, 6, 20, 41, 0, TimeSpan.Zero);

    private readonly IApiClient _api = Substitute.For<IApiClient>();

    public HomePageTests()
    {
        Services.AddRadzenComponents();
        // The setup screens open on the last card played; a substitute storage means every test opens blank.
        Services.AddSingleton(Substitute.For<ILocalStorageService>());
        Services.AddScoped<SetupMemory>();
        Services.AddSingleton(_api);
        // Under bunit JS is loose, so the viewport reports the wide layout.
        Services.AddScoped<Viewport>();
        Services.AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2026, 9, 6, 20, 41, 0, TimeSpan.Zero)));
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Home_offers_the_three_channels_and_navigates_to_them()
    {
        var nav = Services.GetRequiredService<BunitNavigationManager>();
        var cut = Render<Home>();

        var channels = cut.FindAll("article.channel");
        channels.Should().HaveCount(3);
        channels.Select(c => c.GetAttribute("data-channel")).Should().Equal("CPU", "1P", "2P");
        cut.Markup.Should().Contain("CPU vs CPU").And.Contain("Take a side").And.Contain("Both of you");
        cut.Markup.Should().Contain("Sunday", "the eyebrow is the broadcast date line (2026-09-06 is a Sunday)");

        cut.FindAll("article.channel .rz-button")[1].Click();

        nav.Uri.Should().Be(nav.BaseUri + "1p", "the middle channel is the one with a seat in it");
    }

    [Fact]
    public void Home_shows_empty_states_until_there_are_matches_and_fighters()
    {
        var cut = Render<Home>();

        cut.FindAll("[role=status]").Should().HaveCount(2);
        cut.Markup.Should().Contain("No matches yet").And.Contain("Nobody on the card");
    }

    [Fact]
    public async Task Once_there_is_a_past_the_home_page_shows_it_and_leads_back_into_it()
    {
        var fight = MatchId.New();
        _api.GetMatchesAsync(Arg.Any<MatchMode?>(), Arg.Any<CancellationToken>()).Returns(
        [
            new(fight, "u", MatchMode.Fight, Night, Night.AddMinutes(6), "who forgot the bins",
                MatchSide.Human("AB", "Alex"), MatchSide.Human("CD", "Sam"), SessionPhase.Done, SessionStatus.Ready, "Alex", "Close.", IsFake: false),
        ]);
        _api.GetRosterAsync(Arg.Any<CancellationToken>()).Returns(
        [
            FighterStatsDto.Empty("AB", "Alex") with { Fights = 3, Wins = 2, Losses = 1 },
            FighterStatsDto.Empty("ZZ", "Never") with { Fights = 0 },
        ]);

        var cut = Render<Home>();

        await cut.WaitForAssertionAsync(() => cut.FindComponents<ModeBadge>().Should().ContainSingle());
        cut.Markup.Should().Contain("who forgot the bins").And.Contain("Alex vs Sam");
        cut.Find("a.replay").GetAttribute("href").Should().Contain($"verdict/{fight.Value}", "a fight leads to its verdict");
        cut.Markup.Should().Contain("2–1 over 3");
        cut.Markup.Should().NotContain("Never", "somebody who has not argued is not a top fighter");
    }
}
