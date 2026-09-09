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
        // Under bunit JS is loose, so the viewport reports the wide layout.
        Services.AddScoped<Viewport>();
        // Program.cs loads the gate before the first render; under bunit the component's own fallback load does it.
        Services.AddSingleton<FeatureGate>();
        Services.AddSingleton(_simulation);
        JSInterop.Mode = JSRuntimeMode.Loose;
        _api.GetProfilesAsync(Arg.Any<CancellationToken>()).Returns(Cast());
        _api.GetFeaturesAsync(Arg.Any<CancellationToken>()).Returns(new FeatureFlagsDto(true, true, HumanInWatch: true, FishVoices: false));
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
    public async Task The_match_cannot_start_until_both_sides_are_chosen()
    {
        var cut = RenderSetup();

        cut.Markup.Should().Contain("Pick both sides.");
        CanGoOn(cut).Should().BeFalse();

        await ChooseAsync(cut, wife: false, MatchSide.Persona("MAH", "Matthew"));
        CanGoOn(cut).Should().BeFalse("one side is still empty");

        await ChooseAsync(cut, wife: true, MatchSide.Persona("KSH", "Kimberly"));
        CanGoOn(cut).Should().BeTrue();
        (await StartButtonAsync(cut)).HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public async Task Starting_carries_the_matchup_and_the_topic_to_the_play_screen()
    {
        var cut = RenderSetup(onePlayer: true);
        var nav = Services.GetRequiredService<BunitNavigationManager>();

        await ChooseOpponentAsync(cut, "MAH");
        await TypeTagAsync(cut, "KD");
        await (await TopicBoxAsync(cut)).ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "the thermostat" });
        await (await StartButtonAsync(cut)).ClickAsync(new());

        _simulation.IsReady.Should().BeTrue();
        _simulation.Husband!.Id.Should().Be("MAH", "the profile keeps its own seat");
        _simulation.Wife!.IsHuman.Should().BeTrue("which leaves the other one for you");
        _simulation.Human!.Id.Should().Be("KD", "the person's tag is what the debate is recorded under");
        _simulation.Topic.Should().Be("the thermostat");
        nav.Uri.Should().EndWith("watch/play");
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

    /// <summary>
    /// The setup is two steps — who, then what about — so the button that starts an argument is on the second one
    /// and reaching it is part of using the page. Radzen refuses a step the page has said is not ready.
    /// </summary>
    private static async Task<IElement> StartButtonAsync(IRenderedComponent<WatchPage> cut)
    {
        if (cut.FindAll("button.rz-steps-next").SingleOrDefault() is { } next && !next.HasAttribute("disabled"))
        {
            await next.ClickAsync(new());
        }

        return cut.FindAll("button").Single(b => b.TextContent.Contains("Start the argument", StringComparison.Ordinal));
    }

    /// <summary>Whether the page will let anyone past the cast. The reason it will not is stated beside it.</summary>
    private static bool CanGoOn(IRenderedComponent<WatchPage> cut) =>
        cut.FindAll("button.rz-steps-next").SingleOrDefault() is { } next && !next.HasAttribute("disabled");

    /// <summary>The topic is the second step's question, so getting to it is part of asking it.</summary>
    private static async Task<IElement> TopicBoxAsync(IRenderedComponent<WatchPage> cut)
    {
        if (cut.FindAll("input[name=topic]").SingleOrDefault() is null)
        {
            await cut.FindAll("button.rz-steps-next").Single().ClickAsync(new());
        }

        return cut.Find("input[name=topic]");
    }
}
