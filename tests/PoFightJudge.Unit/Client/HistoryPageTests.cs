using Blazored.LocalStorage;
using Bunit;
using Bunit.Rendering;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PoFightJudge.Client.Components;
using PoFightJudge.Client.Pages;
using PoFightJudge.Client.Services;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;
using Radzen;
using Radzen.Blazor;
using FightersPage = PoFightJudge.Client.Pages.Fighters;
using ProfilesPage = PoFightJudge.Client.Pages.Profiles;

namespace PoFightJudge.Unit.Client;

/// <summary>
/// The history page: two kinds of argument in one list, and the one destructive thing in the app.
/// </summary>
public class HistoryPageTests : BunitContext
{
    private static readonly DateTimeOffset Night = new(2026, 9, 1, 20, 0, 0, TimeSpan.Zero);

    private readonly IApiClient _api = Substitute.For<IApiClient>();
    private readonly MatchId _watch = MatchId.New();
    private readonly MatchId _fight = MatchId.New();

    public HistoryPageTests()
    {
        Services.AddRadzenComponents();
        // The setup screens open on the last card played; a substitute storage means every test opens blank.
        Services.AddSingleton(Substitute.For<ILocalStorageService>());
        Services.AddScoped<SetupMemory>();
        Services.AddSingleton(_api);
        // Under bunit JS is loose, so the viewport reports the wide layout — the one that declares every column.
        Services.AddScoped<Viewport>();
        JSInterop.Mode = JSRuntimeMode.Loose;
        _api.GetMatchPageAsync(Arg.Any<MatchQuery>(), Arg.Any<CancellationToken>()).Returns(_ => Page(Both()));
    }

    /// <summary>One page holding everything given to it: the tests are about the screen, not about paging arithmetic.</summary>
    private static MatchPageDto Page(IReadOnlyList<MatchDto> matches) =>
        new(matches, matches.Count, 0, MatchQuery.DefaultTake);

    private IReadOnlyList<MatchDto> Both() =>
    [
        Match(_watch, MatchMode.Watch, "the thermostat", MatchSide.Persona("MAH", "Married Husband"), MatchSide.Persona("KSH", "Karen")),
        Match(_fight, MatchMode.Fight, "who forgot the bins", MatchSide.Human("AB"), MatchSide.Human("CD")),
    ];

    private static MatchDto Match(MatchId id, MatchMode mode, string topic, MatchSide one, MatchSide two) =>
        new(id, "u", mode, Night, Night.AddMinutes(6), topic, one, two, SessionPhase.Done, SessionStatus.Ready, one.DisplayName, "It was close.", IsFake: false);

    /// <summary>
    /// The page under a dialog host, because the confirm the delete button raises is part of what is being tested:
    /// answering it through a service hook would test the mock rather than the button somebody presses.
    /// </summary>
    private IRenderedComponent<ContainerFragment> RenderHistory()
    {
        var cut = Render(builder =>
        {
            builder.OpenComponent<RadzenDialog>(0);
            builder.CloseComponent();
            builder.OpenComponent<History>(1);
            builder.CloseComponent();
        });

        cut.WaitForAssertion(() => cut.FindAll(".history tbody tr").Should().HaveCount(2));
        return cut;
    }

    /// <summary>Answers the confirm the way a person would: by pressing one of its two buttons.</summary>
    private static async Task AnswerConfirmAsync(IRenderedComponent<ContainerFragment> cut, bool answer)
    {
        await cut.WaitForAssertionAsync(() => cut.FindAll(".rz-dialog button").Should().NotBeEmpty());
        var button = cut.FindAll(".rz-dialog button")
            .First(b => b.TextContent.Contains(answer ? "Delete" : "Keep it", StringComparison.Ordinal));
        await button.ClickAsync(new());
    }

    [Fact]
    public void Both_kinds_of_argument_are_listed_and_each_says_which_it_was()
    {
        var cut = RenderHistory();

        cut.FindComponents<ModeBadge>().Select(b => b.Instance.Mode)
            .Should().BeEquivalentTo([MatchMode.Watch, MatchMode.Fight]);
        cut.Markup.Should().Contain("the thermostat").And.Contain("who forgot the bins");
    }

    [Fact]
    public async Task Narrowing_to_one_kind_asks_the_server_rather_than_hiding_rows()
    {
        var cut = RenderHistory();
        _api.ClearReceivedCalls();
        _api.GetMatchPageAsync(Arg.Is<MatchQuery>(q => q.Mode == MatchMode.Fight), Arg.Any<CancellationToken>())
            .Returns(Page([Both()[1]]));

        await cut.FindAll("button").First(b => b.TextContent.Contains("Fights", StringComparison.Ordinal)).ClickAsync(new());

        await _api.Received(1).GetMatchPageAsync(Arg.Is<MatchQuery>(q => q.Mode == MatchMode.Fight), Arg.Any<CancellationToken>());
        await cut.WaitForAssertionAsync(() => cut.FindAll(".history tbody tr").Should().HaveCount(1));
    }

    [Fact]
    public async Task Deleting_asks_first_and_a_no_leaves_everything_where_it_was()
    {
        var cut = RenderHistory();

        // The click does not return until the confirm is answered, so the answer comes while it is still in flight.
        var clicked = cut.Find("tbody tr button[aria-label='Delete the thermostat']").ClickAsync(new());
        await AnswerConfirmAsync(cut, answer: false);
        await clicked;

        await _api.DidNotReceive().DeleteMatchAsync(_watch, Arg.Any<CancellationToken>());
        cut.FindAll(".history tbody tr").Should().HaveCount(2);
    }

    [Fact]
    public async Task A_yes_deletes_the_argument_and_takes_the_row_with_it()
    {
        var cut = RenderHistory();

        var clicked = cut.Find("tbody tr button[aria-label='Delete the thermostat']").ClickAsync(new());
        await AnswerConfirmAsync(cut, answer: true);
        await clicked;

        await _api.Received(1).DeleteMatchAsync(_watch, Arg.Any<CancellationToken>());
        await cut.WaitForAssertionAsync(() => cut.FindAll(".history tbody tr").Should().HaveCount(1));
        cut.Markup.Should().NotContain("the thermostat");
    }

    [Fact]
    public void Only_a_fight_can_be_opened_because_only_a_fight_has_a_verdict_to_read()
    {
        var cut = RenderHistory();

        cut.FindAll("tbody tr button").Where(b => b.TextContent.Contains("Open", StringComparison.Ordinal))
            .Should().HaveCount(1);
    }

    [Fact]
    public async Task Nobody_who_has_never_argued_is_shown_an_empty_table()
    {
        _api.GetMatchPageAsync(Arg.Any<MatchQuery>(), Arg.Any<CancellationToken>()).Returns(Page([]));

        var cut = Render<History>();

        await cut.WaitForAssertionAsync(() => cut.FindComponent<EmptyState>().Instance.Title.Should().Be("Nothing yet"));
        cut.Markup.Should().Contain("Start a fight");
    }
}

/// <summary>The two small pieces the records pages are built from.</summary>
public class RecordComponentTests : BunitContext
{
    public RecordComponentTests()
    {
        Services.AddRadzenComponents();
        // The setup screens open on the last card played; a substitute storage means every test opens blank.
        Services.AddSingleton(Substitute.For<ILocalStorageService>());
        Services.AddScoped<SetupMemory>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void A_run_of_scores_is_drawn_left_to_right_with_the_latest_called_out()
    {
        var cut = Render<FormSparkline>(p => p.Add(c => c.Scores, [40, 55, 70]));

        var points = cut.Find("polyline").GetAttribute("points")!.Split(' ');
        points.Should().HaveCount(3);
        points[0].Should().StartWith("0,");
        points[^1].Should().StartWith("100,");
        cut.Find(".last").TextContent.Should().Be("70");
        cut.Find(".form").GetAttribute("aria-label").Should().Be("Recent scores: 40, 55, 70");
    }

    [Fact]
    public void One_score_is_a_flat_line_rather_than_a_dot_nobody_can_see()
    {
        var cut = Render<FormSparkline>(p => p.Add(c => c.Scores, [60]));

        cut.Find("polyline").GetAttribute("points").Should().Be("0,10 100,10");
    }

    [Fact]
    public void Nobody_with_a_record_yet_gets_a_line_drawn_through_nothing()
    {
        var cut = Render<FormSparkline>(p => p.Add(c => c.Scores, []));

        cut.FindAll("polyline").Should().BeEmpty();
        cut.Markup.Should().Contain("No form yet");
    }

    [Theory]
    [InlineData(MatchMode.Watch, "Watch", "theaters")]
    [InlineData(MatchMode.Fight, "Fight", "mic")]
    public void A_badge_names_the_kind_of_argument_and_explains_it(MatchMode mode, string text, string icon)
    {
        var cut = Render<ModeBadge>(p => p.Add(c => c.Mode, mode));

        cut.Find(".mode").TextContent.Should().Contain(text);
        cut.Markup.Should().Contain(icon);
        cut.Find(".mode").GetAttribute("title").Should().NotBeNullOrWhiteSpace();
    }
}

/// <summary>The standings, on the two pages that list the people they rank.</summary>
public class StandingsTests : BunitContext, IAsyncLifetime
{
    private readonly IApiClient _api = Substitute.For<IApiClient>();

    public StandingsTests()
    {
        Services.AddRadzenComponents();
        Services.AddSingleton(Substitute.For<ILocalStorageService>());
        Services.AddScoped<SetupMemory>();
        Services.AddSingleton(_api);
        Services.AddSingleton(TimeProvider.System);
        // A cast card previews the persona's voice, so the page needs the interop even though nothing here clicks it.
        Services.AddScoped<AudioInterop>();
        // Under bunit JS is loose, so the viewport reports the wide layout — the one that declares every column.
        Services.AddScoped<Viewport>();
        JSInterop.Mode = JSRuntimeMode.Loose;
        _api.GetLeaderboardAsync(MatchMode.Watch, Arg.Any<CancellationToken>())
            .Returns([Row("MAH", "Married Husband", 5, 4), Row("KSH", "Karen", 5, 1)]);
        _api.GetLeaderboardAsync(MatchMode.Fight, Arg.Any<CancellationToken>())
            .Returns([Row("AB", "Alex", 3, 2)]);
        _api.GetProfilesAsync(Arg.Any<CancellationToken>()).Returns([]);
        _api.GetRosterAsync(Arg.Any<CancellationToken>()).Returns([]);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>A cast card holds an AudioInterop, which is released asynchronously; bunit's teardown has to be awaited.</summary>
    public new async Task DisposeAsync() => await base.DisposeAsync().ConfigureAwait(false);

    private static LeaderboardRowDto Row(string id, string name, int matches, int wins) =>
        new(id, name, matches, wins, (double)wins / matches, 61.5);

    [Fact]
    public async Task The_cast_standings_are_on_the_page_that_lists_the_cast()
    {
        _api.GetProfilesAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new ProfileDto { Id = ProfileId.From("MAH"), Persona = new CreateProfileRequest { Initials = "MAH", Name = "Married Husband", Role = ProfileRole.Husband } },
        ]);

        var cut = Render<ProfilesPage>();

        // The order the server sent is the standing, and the places are numbered from it.
        await cut.WaitForAssertionAsync(() => cut.FindComponent<Board>().Instance.Rows
            .Select(r => r.DisplayName).Should().Equal("Married Husband", "Karen"));
        cut.FindComponent<Board>().Markup.Should().Contain("80%", "four wins from five is what the row says");
    }

    [Fact]
    public async Task The_fighter_standings_are_on_the_page_that_lists_the_fighters()
    {
        _api.GetRosterAsync(Arg.Any<CancellationToken>()).Returns(
        [
            FighterStatsDto.Empty("AB", "Alex") with { Fights = 3, Wins = 2, Losses = 1 },
        ]);

        var cut = Render<FightersPage>();

        await cut.WaitForAssertionAsync(() => cut.FindComponent<Board>().Instance.Rows
            .Select(r => r.DisplayName).Should().Equal("Alex"));
    }
}
