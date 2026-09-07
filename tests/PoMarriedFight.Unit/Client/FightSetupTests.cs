using AngleSharp.Dom;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PoMarriedFight.Client.Components;
using PoMarriedFight.Client.Services;
using PoMarriedFight.Shared.Identifiers;
using PoMarriedFight.Shared.Models;
using Radzen;

// The domain tests own PoMarriedFight.Unit.Fight, which shadows the page type here.
using FightPage = PoMarriedFight.Client.Pages.Fight;

namespace PoMarriedFight.Unit.Client;

public class FightSetupTests : BunitContext
{
    private readonly IApiClient _api = Substitute.For<IApiClient>();

    public FightSetupTests()
    {
        Services.AddRadzenComponents();
        Services.AddSingleton(_api);
        JSInterop.Mode = JSRuntimeMode.Loose;
        _api.GetFightersAsync(Arg.Any<CancellationToken>()).Returns(Roster());
    }

    private static IReadOnlyList<FighterDto> Roster() =>
    [
        new("AB", "Alex", DateTimeOffset.UnixEpoch, new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero)),
    ];

    private IRenderedComponent<FightPage> RenderSetup()
    {
        var cut = Render<FightPage>();
        cut.WaitForAssertion(() => cut.FindAll("article.fighter").Should().HaveCount(2));
        return cut;
    }

    private static async Task TypeTagAsync(IRenderedComponent<FightPage> cut, bool second, string tag)
    {
        var input = cut.FindComponents<FighterTagInput>().Single(t => string.Equals(t.Instance.Name, second ? "player2Tag" : "player1Tag", StringComparison.Ordinal));
        await cut.InvokeAsync(() => input.Instance.TagChanged.InvokeAsync(tag));
    }

    private static IElement StartButton(IRenderedComponent<FightPage> cut) =>
        cut.FindAll("button").Single(b => b.TextContent.Contains("Start the fight", StringComparison.Ordinal));

    [Fact]
    public void The_referee_is_hosting_unless_somebody_picks_otherwise()
    {
        var cut = RenderSetup();

        cut.FindComponent<HostPersonaPicker>().Instance.Persona.Should().Be(HostPersonaId.Referee);
        cut.Markup.Should().Contain("The Referee").And.Contain("Roastmaster", "every host is on offer");
    }

    [Fact]
    public void The_fight_cannot_start_until_both_fighters_have_their_own_tag()
    {
        var cut = RenderSetup();

        cut.Markup.Should().Contain("Both fighters need a tag.");
        StartButton(cut).HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public async Task Two_people_cannot_share_one_tag()
    {
        var cut = RenderSetup();

        await TypeTagAsync(cut, second: false, "AB");
        await TypeTagAsync(cut, second: true, "AB");

        cut.Markup.Should().Contain("Two people cannot share one tag.");
        StartButton(cut).HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public async Task A_tag_the_room_has_met_before_is_shown_as_such_and_a_new_one_says_so()
    {
        var cut = RenderSetup();

        await TypeTagAsync(cut, second: false, "AB");
        await TypeTagAsync(cut, second: true, "ZZ");

        var cards = cut.FindComponents<FighterCard>().ToList();
        cards[0].Instance.Fighter.Should().NotBeNull("this tag has argued before");
        cut.Markup.Should().Contain("Alex");
        cards[1].Instance.Fighter.Should().BeNull();
        cut.Markup.Should().Contain("New fighter");
    }

    [Fact]
    public async Task Starting_sends_the_tags_the_host_and_the_topic_and_goes_to_the_fight()
    {
        var id = MatchId.New();
        _api.StartFightAsync(Arg.Any<CreateFightRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new CreateFightResponse(id)));
        var nav = Services.GetRequiredService<BunitNavigationManager>();
        var cut = RenderSetup();

        await TypeTagAsync(cut, second: false, "AB");
        await TypeTagAsync(cut, second: true, "CD");
        await cut.Find("input[name=topic]").InputAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "who does the dishes" });
        await cut.InvokeAsync(() => cut.FindComponent<HostPersonaPicker>().Instance.PersonaChanged.InvokeAsync(HostPersonaId.Roastmaster));
        await StartButton(cut).ClickAsync(new());

        await _api.Received(1).StartFightAsync(
            Arg.Is<CreateFightRequest>(r => r.Player1Tag == "AB" && r.Player2Tag == "CD"
                && r.Topic == "who does the dishes" && r.Persona == HostPersonaId.Roastmaster),
            Arg.Any<CancellationToken>());
        nav.Uri.Should().EndWith($"fight/{id.Value}");
    }

    [Fact]
    public async Task A_blank_topic_is_left_for_the_host_to_ask_about()
    {
        _api.StartFightAsync(Arg.Any<CreateFightRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new CreateFightResponse(MatchId.New())));
        var cut = RenderSetup();

        await TypeTagAsync(cut, second: false, "AB");
        await TypeTagAsync(cut, second: true, "CD");
        await StartButton(cut).ClickAsync(new());

        await _api.Received(1).StartFightAsync(Arg.Is<CreateFightRequest>(r => r.Topic == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_fight_the_server_refuses_says_why_and_stays_on_the_setup_screen()
    {
        _api.StartFightAsync(Arg.Any<CreateFightRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<CreateFightResponse>(new ApiException(400, "That host has left the building.", null)));
        var nav = Services.GetRequiredService<BunitNavigationManager>();
        var startingUri = nav.Uri;
        var cut = RenderSetup();

        await TypeTagAsync(cut, second: false, "AB");
        await TypeTagAsync(cut, second: true, "CD");
        await StartButton(cut).ClickAsync(new());

        await cut.WaitForAssertionAsync(() => cut.Markup.Should().Contain("That host has left the building."));
        nav.Uri.Should().Be(startingUri);
        StartButton(cut).HasAttribute("disabled").Should().BeFalse("it can be tried again");
    }

    [Fact]
    public void The_microphone_is_not_opened_before_anyone_presses_start()
    {
        var cut = RenderSetup();

        cut.Markup.Should().Contain("only opens once you press start");
        JSInterop.Invocations.Should().NotContain(i => i.Identifier.StartsWith("PoLive", StringComparison.Ordinal));
    }
}
