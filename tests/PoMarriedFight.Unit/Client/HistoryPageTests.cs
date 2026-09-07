using Bunit;
using Bunit.Rendering;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PoMarriedFight.Client.Components;
using PoMarriedFight.Client.Pages;
using PoMarriedFight.Client.Services;
using PoMarriedFight.Shared.Identifiers;
using PoMarriedFight.Shared.Models;
using Radzen;
using Radzen.Blazor;

namespace PoMarriedFight.Unit.Client;

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
        Services.AddSingleton(_api);
        JSInterop.Mode = JSRuntimeMode.Loose;
        _api.GetMatchesAsync(Arg.Any<MatchMode?>(), Arg.Any<CancellationToken>()).Returns(_ => Both());
    }

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

        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Should().HaveCount(2));
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
        _api.GetMatchesAsync(MatchMode.Fight, Arg.Any<CancellationToken>()).Returns([Both()[1]]);

        await cut.FindAll("button").First(b => b.TextContent.Contains("Fights", StringComparison.Ordinal)).ClickAsync(new());

        await _api.Received(1).GetMatchesAsync(MatchMode.Fight, Arg.Any<CancellationToken>());
        await cut.WaitForAssertionAsync(() => cut.FindAll("tbody tr").Should().HaveCount(1));
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
        cut.FindAll("tbody tr").Should().HaveCount(2);
    }

    [Fact]
    public async Task A_yes_deletes_the_argument_and_takes_the_row_with_it()
    {
        var cut = RenderHistory();

        var clicked = cut.Find("tbody tr button[aria-label='Delete the thermostat']").ClickAsync(new());
        await AnswerConfirmAsync(cut, answer: true);
        await clicked;

        await _api.Received(1).DeleteMatchAsync(_watch, Arg.Any<CancellationToken>());
        await cut.WaitForAssertionAsync(() => cut.FindAll("tbody tr").Should().HaveCount(1));
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
        _api.GetMatchesAsync(Arg.Any<MatchMode?>(), Arg.Any<CancellationToken>()).Returns([]);

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

/// <summary>The two boards, and the persona page one of them leads to.</summary>
public class LeaderboardPageTests : BunitContext
{
    private readonly IApiClient _api = Substitute.For<IApiClient>();

    public LeaderboardPageTests()
    {
        Services.AddRadzenComponents();
        Services.AddSingleton(_api);
        JSInterop.Mode = JSRuntimeMode.Loose;
        _api.GetLeaderboardAsync(MatchMode.Watch, Arg.Any<CancellationToken>())
            .Returns([Row("MAH", "Married Husband", 5, 4), Row("KSH", "Karen", 5, 1)]);
        _api.GetLeaderboardAsync(MatchMode.Fight, Arg.Any<CancellationToken>())
            .Returns([Row("AB", "Alex", 3, 2)]);
    }

    private static LeaderboardRowDto Row(string id, string name, int matches, int wins) =>
        new(id, name, matches, wins, (double)wins / matches, 61.5);

    [Fact]
    public async Task Both_boards_are_in_the_page_so_one_can_be_read_against_the_other()
    {
        var cut = Render<Leaderboard>();

        await cut.WaitForAssertionAsync(() => cut.FindComponents<Board>().Should().HaveCount(2));
        cut.Markup.Should().Contain("Married Husband").And.Contain("Alex");
    }

    [Fact]
    public async Task The_order_the_server_sent_is_the_standing_and_the_places_are_numbered_from_it()
    {
        var cut = Render<Leaderboard>();

        await cut.WaitForAssertionAsync(() => cut.FindComponents<Board>().Should().HaveCount(2));
        var cast = cut.FindComponents<Board>()[0];
        cast.Instance.Rows.Select(r => r.DisplayName).Should().Equal("Married Husband", "Karen");
        cast.Markup.Should().Contain("80%", "four wins from five is what the row says");
    }

    [Fact]
    public async Task A_board_says_why_it_is_empty_rather_than_showing_an_empty_table()
    {
        _api.GetLeaderboardAsync(Arg.Any<MatchMode>(), Arg.Any<CancellationToken>()).Returns([]);

        var cut = Render<Leaderboard>();

        await cut.WaitForAssertionAsync(() => cut.FindComponents<EmptyState>().Should().HaveCount(2));
        cut.Markup.Should().Contain("argued twice");
    }

    [Fact]
    public async Task Only_the_cast_board_leads_anywhere_because_only_a_persona_has_a_page_yet()
    {
        var cut = Render<Leaderboard>();

        await cut.WaitForAssertionAsync(() => cut.FindComponents<Board>().Should().HaveCount(2));
        var boards = cut.FindComponents<Board>();
        boards[0].Instance.OnOpen.HasDelegate.Should().BeTrue();
        boards[1].Instance.OnOpen.HasDelegate.Should().BeFalse();
    }
}

/// <summary>One persona's record, read back from their results.</summary>
public class ProfileRecordPageTests : BunitContext
{
    private readonly IApiClient _api = Substitute.For<IApiClient>();

    public ProfileRecordPageTests()
    {
        Services.AddRadzenComponents();
        Services.AddSingleton(_api);
        JSInterop.Mode = JSRuntimeMode.Loose;
        _api.GetProfileAsync(ProfileId.From("MAH"), Arg.Any<CancellationToken>()).Returns(new ProfileDto
        {
            Id = ProfileId.From("MAH"),
            Persona = new CreateProfileRequest { Initials = "MAH", Name = "Married Husband", Role = ProfileRole.Husband },
        });
    }

    private static ProfileRecordDto Record(int matches) => new(
        "MAH", matches, 3, 1, 1, 62.5, new DateTimeOffset(2026, 9, 1, 20, 0, 0, TimeSpan.Zero),
        new AdvancedStatsDto(70, 40, 55, 2, 30, 60, 45, 35, 50, 0.5),
        new RivalryDto("KSH", 4, 3, 1));

    private IRenderedComponent<ProfileRecord> RenderRecord() =>
        Render<ProfileRecord>(p => p.Add(c => c.Initials, "MAH"));

    [Fact]
    public async Task The_record_is_the_tally_the_rival_and_what_the_averages_say_about_them()
    {
        _api.GetProfileRecordAsync(ProfileId.From("MAH"), Arg.Any<CancellationToken>()).Returns(Record(5));

        var cut = RenderRecord();

        await cut.WaitForAssertionAsync(() => cut.FindAll(".stats .stat").Should().HaveCount(10, "every measured field is shown"));
        cut.Markup.Should().Contain("Married Husband").And.Contain("KSH").And.Contain("62.5");
        cut.Find(".tally .big").TextContent.Should().Contain("3").And.Contain("of 5");
    }

    [Fact]
    public async Task A_persona_that_has_never_argued_is_offered_an_argument_rather_than_a_table_of_zeroes()
    {
        _api.GetProfileRecordAsync(ProfileId.From("MAH"), Arg.Any<CancellationToken>()).Returns(ProfileRecordDto.Empty("MAH"));

        var cut = RenderRecord();

        await cut.WaitForAssertionAsync(() => cut.FindComponent<EmptyState>().Instance.Title.Should().Be("Never argued"));
        cut.FindAll(".stats .stat").Should().BeEmpty();
    }

    [Fact]
    public async Task Initials_nobody_in_the_cast_uses_are_not_a_record()
    {
        // Matched on the real id: an uninitialised value object is not a valid argument matcher.
        _api.GetProfileRecordAsync(ProfileId.From("ZZZ"), Arg.Any<CancellationToken>()).Returns((ProfileRecordDto?)null);

        var cut = Render<ProfileRecord>(p => p.Add(c => c.Initials, "ZZZ"));

        await cut.WaitForAssertionAsync(() => cut.FindComponent<EmptyState>().Instance.Title.Should().Be("No such persona"));
    }
}
