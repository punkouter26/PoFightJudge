using AngleSharp.Dom;
using Blazored.LocalStorage;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PoFightJudge.Client.Components;
using PoFightJudge.Client.Pages;
using PoFightJudge.Client.Services;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;
using Radzen;

// The domain tests own the namespace PoFightJudge.Unit.Watch, which shadows the page type here.
using WatchPage = PoFightJudge.Client.Pages.Watch;

namespace PoFightJudge.Unit.Client;

public class WatchSetupTests : BunitContext
{
    private readonly IApiClient _api = Substitute.For<IApiClient>();
    private readonly SimulationState _simulation = new();

    public WatchSetupTests()
    {
        Services.AddRadzenComponents();
        // The setup screens open on the last card played; a substitute storage means every test opens blank.
        Services.AddSingleton(Substitute.For<ILocalStorageService>());
        Services.AddScoped<SetupMemory>();
        Services.AddSingleton(_api);
        // Program.cs loads the gate before the first render; under bunit the component's own fallback load does it.
        Services.AddSingleton<FeatureGate>();
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

    /// <summary>
    /// The setup screen at whichever channel it is being asked for. One component serves both: CPU is two
    /// profiles arguing, 1P is the same argument with one seat taken by the person watching.
    /// </summary>
    private IRenderedComponent<WatchPage> RenderSetup(bool onePlayer = false)
    {
        if (onePlayer)
        {
            Services.GetRequiredService<BunitNavigationManager>().NavigateTo("1p");
        }

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
    public void The_cast_is_offered_per_role_on_both_channels()
    {
        var cut = RenderSetup();

        var husbandPicker = cut.FindComponents<ProfilePicker>().Single(p => !p.Instance.IsWife);

        husbandPicker.Instance.Cast.Should().HaveCount(3, "the picker filters by role itself");
        cut.Markup.Should().Contain("Nobody yet", "neither side is chosen yet");
    }

    /// <summary>
    /// CPU is two profiles arguing and nothing else: taking a seat is what 1P is for, so the option to be one of
    /// the two is not offered here at all.
    /// </summary>
    [Fact]
    public void Nobody_can_take_a_seat_on_the_channel_that_is_two_profiles_arguing()
    {
        var cut = RenderSetup();

        cut.FindComponents<ProfilePicker>().Should().OnlyContain(p => !p.Instance.AllowHuman);
        cut.Markup.Should().Contain("CPU vs CPU");
    }

    /// <summary>
    /// 1P asks one question — who are you arguing with — over the whole cast, and states which seat that leaves
    /// you. Nobody picks a side twice, and "a person at the microphone" is not one of the choices: you are the
    /// person, and the only thing left to say is what you argue under.
    /// </summary>
    [Fact]
    public void One_player_asks_only_who_you_are_arguing_with()
    {
        var cut = RenderSetup(onePlayer: true);

        var picker = cut.FindComponents<ProfilePicker>().Should().ContainSingle().Subject;
        picker.Instance.AnyRole.Should().BeTrue("husbands and wives are both somebody to argue with");
        picker.Instance.AllowHuman.Should().BeFalse("you are the person; that is the channel");
        picker.Instance.Cast.Should().HaveCount(3);
        cut.Markup.Should().Contain("1P").And.Contain("Who are you arguing with?");
        cut.FindAll("input[name=yourTag]").Should().ContainSingle();
    }

    [Theory]
    [InlineData("MAH", "the wife")]
    [InlineData("KSH", "the husband")]
    public async Task The_seat_you_take_is_whichever_one_your_opponent_leaves(string opponent, string seat)
    {
        var cut = RenderSetup(onePlayer: true);

        await ChooseOpponentAsync(cut, opponent);

        cut.Find(".you .seat").TextContent.Should().Contain(seat);
    }

    [Fact]
    public async Task One_player_will_not_start_until_you_say_who_you_are()
    {
        var cut = RenderSetup(onePlayer: true);

        cut.Markup.Should().Contain("Pick who you are arguing with.");

        await ChooseOpponentAsync(cut, "MAH");
        cut.Markup.Should().Contain("Say who you are");
        StartButton(cut).HasAttribute("disabled").Should().BeTrue();

        await TypeTagAsync(cut, "KD");
        StartButton(cut).HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void A_person_is_only_offered_when_a_transcriber_could_finish_their_turn()
    {
        _api.GetFeaturesAsync(Arg.Any<CancellationToken>()).Returns(new FeatureFlagsDto(true, true, HumanInWatch: false, true));

        var cut = RenderSetup(onePlayer: true);

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
    public async Task The_same_profile_cannot_argue_with_itself()
    {
        var cut = RenderSetup();

        await ChooseAsync(cut, wife: false, MatchSide.Persona("MAH", "Matthew"));
        await ChooseAsync(cut, wife: true, MatchSide.Persona("MAH", "Matthew"));

        cut.Markup.Should().Contain("Both sides cannot be the same.");
        StartButton(cut).HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public async Task Starting_carries_the_matchup_and_the_topic_to_the_play_screen()
    {
        var cut = RenderSetup(onePlayer: true);
        var nav = Services.GetRequiredService<BunitNavigationManager>();

        await ChooseOpponentAsync(cut, "MAH");
        await TypeTagAsync(cut, "KD");
        await cut.Find("input[name=topic]").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "the thermostat" });
        await StartButton(cut).ClickAsync(new());

        _simulation.IsReady.Should().BeTrue();
        _simulation.Husband!.Id.Should().Be("MAH", "the profile keeps its own seat");
        _simulation.Wife!.IsHuman.Should().BeTrue("which leaves the other one for you");
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

    /// <summary>Chooses the opponent on 1P, where there is only one picker.</summary>
    private static async Task ChooseOpponentAsync(IRenderedComponent<WatchPage> cut, string initials)
    {
        var picker = cut.FindComponents<ProfilePicker>().Single();
        var persona = Cast().Single(p => string.Equals(p.Persona.Initials, initials, StringComparison.Ordinal));
        await cut.InvokeAsync(() => picker.Instance.SideChanged.InvokeAsync(MatchSide.Persona(initials, persona.Persona.Name)));
    }

    private static async Task TypeTagAsync(IRenderedComponent<WatchPage> cut, string tag) =>
        await cut.Find("input[name=yourTag]").InputAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = tag });

    private static IElement StartButton(IRenderedComponent<WatchPage> cut) =>
        cut.FindAll("button").Single(b => b.TextContent.Contains("Start the argument", StringComparison.Ordinal));
}
