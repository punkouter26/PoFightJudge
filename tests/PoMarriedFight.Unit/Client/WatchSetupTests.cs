using AngleSharp.Dom;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PoMarriedFight.Client.Components;
using PoMarriedFight.Client.Pages;
using PoMarriedFight.Client.Services;
using PoMarriedFight.Shared.Identifiers;
using PoMarriedFight.Shared.Models;
using Radzen;

// The domain tests own the namespace PoMarriedFight.Unit.Watch, which shadows the page type here.
using WatchPage = PoMarriedFight.Client.Pages.Watch;

namespace PoMarriedFight.Unit.Client;

public class WatchSetupTests : BunitContext
{
    private readonly IApiClient _api = Substitute.For<IApiClient>();
    private readonly SimulationState _simulation = new();

    public WatchSetupTests()
    {
        Services.AddRadzenComponents();
        Services.AddSingleton(_api);
        Services.AddSingleton(_simulation);
        JSInterop.Mode = JSRuntimeMode.Loose;
        _api.GetProfilesAsync(Arg.Any<CancellationToken>()).Returns(Cast());
        _api.GetFeaturesAsync(Arg.Any<CancellationToken>()).Returns(new FeatureFlagsDto(true, true, HumanInWatch: true, true));
    }

    private static IReadOnlyList<ProfileDto> Cast() =>
    [
        Persona("MAH", ProfileRole.Husband, "Matthew"),
        Persona("DJT", ProfileRole.Husband, "Donald"),
        Persona("KSH", ProfileRole.Wife, "Kimberly"),
    ];

    private static ProfileDto Persona(string initials, ProfileRole role, string name) => new()
    {
        Id = ProfileId.From(initials),
        Persona = new CreateProfileRequest { Initials = initials, Role = role, Name = name, Likes = "a", Dislikes = "b" },
    };

    private IRenderedComponent<WatchPage> RenderSetup()
    {
        var cut = Render<WatchPage>();
        cut.WaitForAssertion(() => cut.FindAll("article.speaker").Should().HaveCount(2));
        return cut;
    }

    /// <summary>Chooses a side through the picker the way the dropdown does.</summary>
    private static async Task ChooseAsync(IRenderedComponent<WatchPage> cut, bool wife, MatchSide? side)
    {
        var picker = cut.FindComponents<ProfilePicker>().Single(p => p.Instance.IsWife == wife);
        await cut.InvokeAsync(() => picker.Instance.SideChanged.InvokeAsync(side));
    }

    [Fact]
    public void The_cast_is_offered_per_role_and_a_person_may_take_a_side()
    {
        var cut = RenderSetup();

        var husbandPicker = cut.FindComponents<ProfilePicker>().Single(p => !p.Instance.IsWife);
        var wifePicker = cut.FindComponents<ProfilePicker>().Single(p => p.Instance.IsWife);

        husbandPicker.Instance.Cast.Should().HaveCount(3, "the picker filters by role itself");
        husbandPicker.Instance.AllowHuman.Should().BeTrue();
        wifePicker.Instance.AllowHuman.Should().BeTrue();
        cut.Markup.Should().Contain("Nobody yet", "neither side is chosen yet");
    }

    [Fact]
    public void A_person_is_only_offered_when_a_transcriber_could_finish_their_turn()
    {
        _api.GetFeaturesAsync(Arg.Any<CancellationToken>()).Returns(new FeatureFlagsDto(true, true, HumanInWatch: false, true));

        var cut = RenderSetup();

        cut.FindComponents<ProfilePicker>().Should().OnlyContain(p => !p.Instance.AllowHuman);
    }

    [Fact]
    public async Task The_match_cannot_start_until_both_sides_are_chosen()
    {
        var cut = RenderSetup();

        cut.Markup.Should().Contain("Pick both sides.");
        StartButton(cut).HasAttribute("disabled").Should().BeTrue();

        await ChooseAsync(cut, wife: false, MatchSide.Persona("MAH", "Matthew"));
        StartButton(cut).HasAttribute("disabled").Should().BeTrue("one side is still empty");

        await ChooseAsync(cut, wife: true, MatchSide.Persona("KSH", "Kimberly"));
        StartButton(cut).HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public async Task The_same_side_twice_is_blocked_and_so_are_two_people()
    {
        var cut = RenderSetup();

        await ChooseAsync(cut, wife: false, MatchSide.Persona("MAH", "Matthew"));
        await ChooseAsync(cut, wife: true, MatchSide.Persona("MAH", "Matthew"));
        cut.Markup.Should().Contain("Both sides cannot be the same.");
        StartButton(cut).HasAttribute("disabled").Should().BeTrue();

        await ChooseAsync(cut, wife: false, MatchSide.Human("AB"));
        await ChooseAsync(cut, wife: true, MatchSide.Human("CD"));
        cut.Markup.Should().Contain("Two people arguing is a fight, not a watch.");
        StartButton(cut).HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public async Task Starting_carries_the_matchup_and_the_topic_to_the_play_screen()
    {
        var nav = Services.GetRequiredService<BunitNavigationManager>();
        var cut = RenderSetup();

        await ChooseAsync(cut, wife: false, MatchSide.Persona("MAH", "Matthew"));
        await ChooseAsync(cut, wife: true, MatchSide.Human("KD", "Kim"));
        await cut.Find("input[name=topic]").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "the thermostat" });
        await StartButton(cut).ClickAsync(new());

        _simulation.IsReady.Should().BeTrue();
        _simulation.Husband!.Id.Should().Be("MAH");
        _simulation.Wife!.IsHuman.Should().BeTrue();
        _simulation.Human!.Id.Should().Be("KD", "the person's tag is what the debate is recorded under");
        _simulation.Topic.Should().Be("the thermostat");
        nav.Uri.Should().EndWith("watch/play");
    }

    [Fact]
    public void An_empty_cast_sends_the_visitor_to_build_one()
    {
        _api.GetProfilesAsync(Arg.Any<CancellationToken>()).Returns([]);

        var cut = Render<WatchPage>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("No cast yet"));
        cut.Markup.Should().Contain("Go to profiles");
    }

    private static IElement StartButton(IRenderedComponent<WatchPage> cut) =>
        cut.FindAll("button").Single(b => b.TextContent.Contains("Start the argument", StringComparison.Ordinal));
}
