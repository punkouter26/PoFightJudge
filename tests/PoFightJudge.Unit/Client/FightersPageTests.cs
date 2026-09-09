using Bunit;
using Bunit.Rendering;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PoFightJudge.Client.Components;
using PoFightJudge.Client.Pages;
using PoFightJudge.Client.Services;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;
using Radzen;
using Radzen.Blazor;

// The stats tests own PoFightJudge.Unit.Fighters, which shadows the page type here.
using FightersPage = PoFightJudge.Client.Pages.Fighters;

namespace PoFightJudge.Unit.Client;

/// <summary>The roster: a list of people is only worth reading if it says what each of them has done.</summary>
public class FightersPageTests : BunitContext
{
    private static readonly DateTimeOffset Night = new(2026, 9, 1, 20, 0, 0, TimeSpan.Zero);

    private readonly IApiClient _api = Substitute.For<IApiClient>();

    public FightersPageTests()
    {
        Services.AddRadzenComponents();
        Services.AddSingleton(_api);
        // Under bunit JS is loose, so the viewport reports the wide layout — the one that declares every column.
        Services.AddScoped<Viewport>();
        JSInterop.Mode = JSRuntimeMode.Loose;
        _api.GetRosterAsync(Arg.Any<CancellationToken>()).Returns(
        [
            FighterStatsDto.Empty("AB", "Alex") with { Fights = 3, Wins = 2, Losses = 1, Form = [50, 60, 70], LastFoughtAt = Night },
            FighterStatsDto.Empty("CD", "CD") with { Fights = 1, Losses = 1, Form = [30], LastFoughtAt = Night },
        ]);
    }

    [Fact]
    public async Task Everybody_on_the_card_brings_their_record_and_the_shape_of_their_form()
    {
        var cut = Render<FightersPage>();

        await cut.WaitForAssertionAsync(() => cut.FindAll("tbody tr").Should().HaveCount(2));
        cut.Markup.Should().Contain("Alex").And.Contain("2–1");
        cut.FindComponents<FormSparkline>().Should().HaveCount(2, "a record without a direction is half a record");
    }

    [Fact]
    public async Task Somebody_who_has_never_been_given_a_name_is_said_to_have_none_rather_than_named_after_their_tag()
    {
        var cut = Render<FightersPage>();

        await cut.WaitForAssertionAsync(() => cut.FindAll("tbody tr").Should().HaveCount(2));
        cut.Markup.Should().Contain("no name yet");
    }

    [Fact]
    public async Task A_row_leads_to_that_persons_page()
    {
        var nav = Services.GetRequiredService<BunitNavigationManager>();
        var cut = Render<FightersPage>();
        await cut.WaitForAssertionAsync(() => cut.FindAll("tbody tr").Should().HaveCount(2));

        await cut.Find("tbody tr").ClickAsync(new());

        nav.Uri.Should().Be(nav.BaseUri + "fighters/AB");
    }

    [Fact]
    public async Task An_empty_card_says_how_somebody_gets_onto_it()
    {
        _api.GetRosterAsync(Arg.Any<CancellationToken>()).Returns([]);

        var cut = Render<FightersPage>();

        await cut.WaitForAssertionAsync(() => cut.FindComponent<EmptyState>().Instance.Title.Should().Be("Nobody on the card"));
        cut.Markup.Should().Contain("steps up to the mic");
    }
}

/// <summary>One person's page: their record, how they argue, and the two things they may change about themselves.</summary>
public class FighterProfilePageTests : BunitContext
{
    private static readonly DateTimeOffset Night = new(2026, 9, 1, 20, 0, 0, TimeSpan.Zero);

    private readonly IApiClient _api = Substitute.For<IApiClient>();

    public FighterProfilePageTests()
    {
        Services.AddRadzenComponents();
        Services.AddSingleton(_api);
        // Under bunit JS is loose, so the viewport reports the wide layout — the one that declares every column.
        Services.AddScoped<Viewport>();
        JSInterop.Mode = JSRuntimeMode.Loose;
        _api.GetFighterProfileAsync(FighterId.From("AB"), Arg.Any<CancellationToken>()).Returns(Profile());
    }

    private static FighterProfileDto Profile(string name = "Alex") => new(
        new FighterDto("AB", name, Night.AddMonths(-2), Night),
        FighterStatsDto.Empty("AB", name) with
        {
            Fights = 3,
            Wins = 2,
            Losses = 1,
            AverageScore = 63.3,
            BestScore = 81,
            Streak = 2,
            Form = [40, 70, 81],
            Badges = ["Usually right"],
            TopRival = new RivalryDto("CD", 3, 2, 1),
        },
        new StyleProfileDto("AB", 3, "clipped", ["look, the thing is"], ["Straw man"], "Look, the thing is", 2, "B2",
            ["frustration"], "You never load the dishwasher.", ["Let them finish."], "3 fights. Sounds clipped."),
        [
            new FighterResultDto("AB", "u", MatchId.New(), MatchMode.Fight, Night, "the thermostat", "CD", true, false, 81, StyleSnapshot.Empty),
            new FighterResultDto("AB", "u", MatchId.New(), MatchMode.Watch, Night.AddDays(-1), "the bins", "CD", false, false, 40, StyleSnapshot.Empty),
        ]);

    private IRenderedComponent<FighterProfile> RenderProfile() =>
        Render<FighterProfile>(p => p.Add(c => c.Tag, "ab"));

    [Fact]
    public async Task The_page_carries_the_record_the_badges_the_rival_and_how_they_argue()
    {
        var cut = RenderProfile();

        await cut.WaitForAssertionAsync(() => cut.FindComponent<StyleProfilePanel>().Should().NotBeNull());
        cut.Find(".tally .big").TextContent.Should().Contain("2").And.Contain("of 3");
        cut.Markup.Should().Contain("Usually right").And.Contain("CD").And.Contain("won 2 in a row");
        cut.FindComponent<FormSparkline>().Instance.Scores.Should().Equal(40, 70, 81);
        cut.FindAll("tbody tr").Should().HaveCount(2, "everything they argued is listed");
    }

    [Fact]
    public async Task The_name_can_be_saved_only_once_it_has_actually_changed()
    {
        var cut = RenderProfile();
        await cut.WaitForAssertionAsync(() => cut.FindComponent<RadzenTextBox>().Should().NotBeNull());

        var save = cut.FindAll("button").First(b => b.TextContent.Contains("Save name", StringComparison.Ordinal));
        save.HasAttribute("disabled").Should().BeTrue("nothing has changed yet");

        _api.RenameFighterAsync(FighterId.From("AB"), "Alexandra", Arg.Any<CancellationToken>())
            .Returns(new FighterDto("AB", "Alexandra", Night.AddMonths(-2), Night));
        await cut.InvokeAsync(() => cut.FindComponent<RadzenTextBox>().Instance.ValueChanged.InvokeAsync("Alexandra"));
        await cut.FindAll("button").First(b => b.TextContent.Contains("Save name", StringComparison.Ordinal)).ClickAsync(new());

        await _api.Received(1).RenameFighterAsync(FighterId.From("AB"), "Alexandra", Arg.Any<CancellationToken>());
        await cut.WaitForAssertionAsync(() => cut.Markup.Should().Contain("Alexandra"));
        cut.Markup.Should().Contain("AB", "the tag is the identity and does not move");
    }

    [Fact]
    public async Task Forgetting_somebody_is_confirmed_first_and_says_the_debates_stay()
    {
        var cut = Render(builder =>
        {
            builder.OpenComponent<RadzenDialog>(0);
            builder.CloseComponent();
            builder.OpenComponent<FighterProfile>(1);
            builder.AddComponentParameter(2, nameof(FighterProfile.Tag), "ab");
            builder.CloseComponent();
        });

        await cut.WaitForAssertionAsync(() => cut.FindAll("button").Should().NotBeEmpty());
        var clicked = cut.FindAll("button").First(b => b.TextContent.Contains("Forget them", StringComparison.Ordinal)).ClickAsync(new());

        await cut.WaitForAssertionAsync(() => cut.FindAll(".rz-dialog button").Should().NotBeEmpty());
        cut.Find(".rz-dialog").TextContent.Should().Contain("The debates themselves stay");
        await cut.FindAll(".rz-dialog button").First(b => b.TextContent.Contains("Keep them", StringComparison.Ordinal)).ClickAsync(new());
        await clicked;

        await _api.DidNotReceive().DeleteFighterAsync(FighterId.From("AB"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_tag_nobody_argues_under_is_not_a_person()
    {
        _api.GetFighterProfileAsync(FighterId.From("ZZ"), Arg.Any<CancellationToken>()).Returns((FighterProfileDto?)null);

        var cut = Render<FighterProfile>(p => p.Add(c => c.Tag, "zz"));

        await cut.WaitForAssertionAsync(() => cut.FindComponent<EmptyState>().Instance.Title.Should().Be("No such fighter"));
    }
}

/// <summary>How somebody argues, and how honest the panel is about how little it can know yet.</summary>
public class StyleProfilePanelTests : BunitContext
{
    public StyleProfilePanelTests()
    {
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Nothing_is_claimed_about_somebody_who_has_never_spoken()
    {
        var cut = Render<StyleProfilePanel>(p => p.Add(c => c.Style, StyleProfileDto.Empty("AB")));

        cut.Markup.Should().Contain("Nothing yet").And.Contain("no debates yet");
        cut.FindAll(".chips").Should().BeEmpty();
    }

    [Fact]
    public void A_read_from_one_or_two_debates_says_so_on_its_face()
    {
        var early = new StyleProfileDto("AB", 2, "clipped", [], [], "Look", 1, "B1", [], string.Empty, [], "2 fights so far.");

        var cut = Render<StyleProfilePanel>(p => p.Add(c => c.Style, early));

        cut.Markup.Should().Contain("from 2 debates").And.Contain("early read");
    }

    [Fact]
    public void A_settled_read_names_the_habits_the_words_and_the_line_the_host_gets()
    {
        var style = new StyleProfileDto("AB", 6, "clipped", ["look, the thing is"], ["Straw man"], "Look, the thing is", 4,
            "B2", ["frustration"], "You never load the dishwasher.", ["Let them finish."], "6 fights. Sounds clipped.");

        var cut = Render<StyleProfilePanel>(p => p.Add(c => c.Style, style));

        cut.Markup.Should().NotContain("early read");
        cut.Markup.Should().Contain("look, the thing is").And.Contain("Straw man").And.Contain("frustration");
        cut.Find("blockquote").TextContent.Should().Contain("dishwasher");
        cut.Markup.Should().Contain("Let them finish.").And.Contain("6 fights. Sounds clipped.");
        cut.Markup.Should().Contain("4 times", "an opener said four times is a habit, and the count is what makes it one");
    }
}
