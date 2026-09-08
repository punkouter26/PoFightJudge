using AngleSharp.Dom;
using Blazored.LocalStorage;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PoFightJudge.Client.Components;
using PoFightJudge.Client.Services;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;
using Radzen;
using Radzen.Blazor;

// The domain tests own PoFightJudge.Unit.Fight, which shadows the page type here.
using FightPage = PoFightJudge.Client.Pages.Fight;

namespace PoFightJudge.Unit.Client;

public class FightSetupTests : BunitContext
{
    private readonly IApiClient _api = Substitute.For<IApiClient>();

    public FightSetupTests()
    {
        Services.AddRadzenComponents();
        // The setup screens open on the last card played; a substitute storage means every test opens blank.
        Services.AddSingleton(Substitute.For<ILocalStorageService>());
        Services.AddScoped<SetupMemory>();
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
    public async Task Each_fighter_chooses_the_seat_their_persona_will_argue_from_and_the_choice_is_sent()
    {
        // 2P itself has no husband or wife. The choice is for afterwards: the persona read from this fight is what
        // CPU and 1P put in a seat, and those channels pit a husband against a wife.
        _api.StartFightAsync(Arg.Any<CreateFightRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new CreateFightResponse(MatchId.New())));
        var cut = RenderSetup();

        var seats = cut.FindComponents<RadzenSelectBar<ProfileRole>>();
        seats.Should().HaveCount(2, "one per fighter");
        seats[0].Instance.Value.Should().Be(ProfileRole.Husband, "fighter one is the husband unless they say otherwise");
        seats[1].Instance.Value.Should().Be(ProfileRole.Wife);

        await TypeTagAsync(cut, second: false, "KKK");
        await TypeTagAsync(cut, second: true, "LLL");
        await cut.InvokeAsync(() => seats[0].Instance.ValueChanged.InvokeAsync(ProfileRole.Wife));
        await cut.InvokeAsync(() => seats[1].Instance.ValueChanged.InvokeAsync(ProfileRole.Husband));
        await StartButton(cut).ClickAsync(new());

        await _api.Received(1).StartFightAsync(
            Arg.Is<CreateFightRequest>(r => r.Player1Role == ProfileRole.Wife && r.Player2Role == ProfileRole.Husband),
            Arg.Any<CancellationToken>());
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

    /// <summary>
    /// SPEC §J2 says the tag boxes default to the last pair used. Nothing did: every fight started by typing both
    /// tags again, which is the tax on the same two people arguing most nights.
    /// </summary>
    [Fact]
    public void The_setup_opens_on_the_card_that_was_played_last()
    {
        var storage = Services.GetRequiredService<ILocalStorageService>();
        storage.GetItemAsync<FightCard>(SetupMemory.FightKey, Arg.Any<CancellationToken>())
            .Returns(new FightCard("AB", "CD", "the bins", ProfileRole.Wife, ProfileRole.Husband, HostPersonaId.Referee));

        var cut = RenderSetup();

        cut.WaitForAssertion(() => StartButton(cut).HasAttribute("disabled").Should().BeFalse("both tags came back, so it can start"));
        cut.Markup.Should().Contain("the bins");
    }

    [Fact]
    public async Task Starting_a_fight_remembers_the_card_for_next_time()
    {
        var storage = Services.GetRequiredService<ILocalStorageService>();
        _api.StartFightAsync(Arg.Any<CreateFightRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new CreateFightResponse(MatchId.New())));
        var cut = RenderSetup();
        await TypeTagAsync(cut, second: false, "AB");
        await TypeTagAsync(cut, second: true, "CD");

        await StartButton(cut).ClickAsync(new());

        await storage.Received(1).SetItemAsync(
            SetupMemory.FightKey,
            Arg.Is<FightCard>(c => string.Equals(c.One, "AB", StringComparison.Ordinal) && string.Equals(c.Two, "CD", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }
}
