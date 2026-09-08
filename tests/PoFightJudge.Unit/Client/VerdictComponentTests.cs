using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using PoFightJudge.Client.Components;
using PoFightJudge.Client.Pages;
using PoFightJudge.Client.Services;
using PoFightJudge.Client.Stats;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;
using Radzen;

namespace PoFightJudge.Unit.Client;

/// <summary>
/// The page a fight ends on. Everything measured has to reach it: a number that was counted and then never shown is
/// one nobody can act on.
/// </summary>
public sealed class VerdictComponentTests : BunitContext, IAsyncLifetime
{
    private readonly IApiClient _api = Substitute.For<IApiClient>();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 9, 7, 21, 0, 0, TimeSpan.Zero));
    private readonly MatchId _id = MatchId.New();

    public VerdictComponentTests()
    {
        Services.AddRadzenComponents();
        Services.AddSingleton(_api);
        Services.AddSingleton<TimeProvider>(_clock);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public new async Task DisposeAsync() => await base.DisposeAsync().ConfigureAwait(false);

    private static PlayerMetricsDto Metrics(double share) => new(
        TalkSeconds: 62, TalkShare: share, Turns: 3, AverageTurnSeconds: 20.7, LongestTurnSeconds: 31,
        Words: 180, WordsPerMinute: 174, Pauses: 4, MeanPauseSeconds: 1.4, LongestPauseSeconds: 3.2,
        InterruptionsMade: 2, InterruptionsReceived: 1, OverlapSeconds: 4.5, HostInterrupts: 1,
        FillersPer100: 3.2, Hedges: 5, Absolutes: 4, Questions: 6, TypeTokenRatio: 0.62, MeanWordLength: 4.4,
        SyllablesPerWord: 1.4, MeanSentenceLength: 13.2, FleschReadingEase: 68.4, FleschKincaidGrade: 7.1,
        TopWords: ["thermostat", "always"], LongestWord: "unreasonable", RepeatedPhrases: ["that is not (×3)"],
        Profanity: 1, FirstPersonRatio: 0.18, SecondPersonRatio: 0.31);

    private static PlayerAssessmentDto Assessment(string best) => new(
        "B2", "clear but hedged", 2, ["a slip"], 6, 7, 8,
        [new FallacyDto("Straw man", "you always say that")], 5, 6, 7, 80,
        [new ClaimDto("The heating was on all night", "False", "The timer says otherwise")],
        new EmotionProfileDto(20, 40, 25, 10, 5, 0), [new EmotionPointDto(30, "frustrated", 7)],
        "peak", ["dry", "quick", "certain"], "quick", "even", "varied",
        7, 6, 4, 5, best, "worst", ["one", "two", "three"]);

    private AnalysisReportDto Report() => new(
        _id, "the thermostat",
        new PlayerReportDto("AL", Metrics(0.6), Assessment("that is not what I said")),
        new PlayerReportDto("SM", Metrics(0.4), Assessment("you left it on all night")),
        new OverallVerdictDto(Speaker.Player1, Speaker.Player2, Speaker.Player1, ["clearer", "kinder", "right"], "One of them listened."),
        "The host called it for AL.", false, string.Empty, 42.5,
        new DateTimeOffset(2026, 9, 7, 20, 55, 0, TimeSpan.Zero));

    private IRenderedComponent<PoFightJudge.Client.Pages.Verdict> RenderReady()
    {
        _api.GetAnalysisAsync(_id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AnalysisResponse(AnalysisStatus.Ready, null, Report())));
        var cut = Render<PoFightJudge.Client.Pages.Verdict>(p => p.Add(v => v.MatchIdText, _id.Value));
        cut.WaitForAssertion(() => cut.FindAll("section.ruling").Should().HaveCount(1));
        return cut;
    }

    [Fact]
    public void The_ruling_names_who_took_it_and_why()
    {
        var cut = RenderReady();

        cut.Find("section.ruling").TextContent.Should().Contain("AL took it");
        cut.Find(".summary").TextContent.Should().Contain("One of them listened");
        cut.FindAll(".reasons li").Should().HaveCount(3);
    }

    [Fact]
    public void Every_measured_number_reaches_the_page()
    {
        var cut = RenderReady();
        var shown = cut.Markup;

        // The formatter is the one place each metric is described, so covering it covers the page.
        var described = StatFormat.Describe(Metrics(0.6));
        var expected = typeof(PlayerMetricsDto).GetProperties().Length;
        described.Should().HaveCount(expected, "a metric with no description would never be shown");

        foreach (var stat in described)
        {
            shown.Should().Contain(stat.Label, $"{stat.Label} was measured and should be visible");
        }
    }

    [Fact]
    public void Every_judged_trait_is_shown_as_well_as_scored()
    {
        var cut = RenderReady();

        var traits = cut.FindComponents<TraitScores>();
        traits.Should().HaveCount(2, "both of them are assessed");
        foreach (var (label, _, _) in StatFormat.Traits(Assessment("q")))
        {
            cut.Markup.Should().Contain(label);
        }
    }

    [Fact]
    public void Both_of_them_get_a_card_and_only_the_winner_is_marked_as_such()
    {
        var cut = RenderReady();

        var cards = cut.FindComponents<PlayerCard>().ToList();
        cards.Should().HaveCount(2);
        cards[0].Instance.Won.Should().BeTrue();
        cards[1].Instance.Won.Should().BeFalse();
        cut.Find(".player[data-tag=AL]").TextContent.Should().Contain("that is not what I said");
    }

    [Fact]
    public void Claims_and_the_logic_that_slipped_are_both_laid_out()
    {
        var cut = RenderReady();

        cut.Markup.Should().Contain("The heating was on all night");
        cut.Markup.Should().Contain("Straw man");
        cut.Markup.Should().Contain("The timer says otherwise");
    }

    [Fact]
    public void Advice_is_given_to_each_of_them_separately()
    {
        var cut = RenderReady();

        cut.FindAll(".tips li").Should().HaveCount(6, "three each");
        cut.FindAll(".worst").Should().HaveCount(2);
    }

    [Fact]
    public void An_uncertain_attribution_is_said_out_loud_rather_than_hidden()
    {
        _api.GetAnalysisAsync(_id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AnalysisResponse(
                AnalysisStatus.Ready,
                null,
                Report() with { SpeakerMappingFlagged = true, SpeakerMappingNote = "Two voices could not be told apart." })));

        var cut = Render<PoFightJudge.Client.Pages.Verdict>(p => p.Add(v => v.MatchIdText, _id.Value));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("not certain"));
        cut.Markup.Should().Contain("Two voices could not be told apart");
    }

    [Fact]
    public void While_it_is_being_read_the_page_says_what_it_is_waiting_for()
    {
        _api.GetAnalysisAsync(_id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AnalysisResponse(AnalysisStatus.Judging, null, null)));

        var cut = Render<PoFightJudge.Client.Pages.Verdict>(p => p.Add(v => v.MatchIdText, _id.Value));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Reading the argument"));
        cut.FindAll("section.ruling").Should().BeEmpty();
    }

    [Fact]
    public async Task A_reading_that_failed_says_so_and_can_be_sent_back()
    {
        _api.GetAnalysisAsync(_id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AnalysisResponse(AnalysisStatus.Failed, "The judge is out.", null)));
        var cut = Render<PoFightJudge.Client.Pages.Verdict>(p => p.Add(v => v.MatchIdText, _id.Value));
        await cut.WaitForAssertionAsync(() => cut.Markup.Should().Contain("The judge is out."));

        _api.GetAnalysisAsync(_id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AnalysisResponse(AnalysisStatus.Ready, null, Report())));
        await cut.FindAll("button").Single(b => b.TextContent.Contains("Read it again", StringComparison.Ordinal)).ClickAsync(new());

        await cut.WaitForAssertionAsync(() => cut.FindAll("section.ruling").Should().HaveCount(1));
        await _api.Received(1).RetryAnalysisAsync(_id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void A_fight_that_is_not_one_is_refused_before_anything_is_asked_for()
    {
        var cut = Render<PoFightJudge.Client.Pages.Verdict>(p => p.Add(v => v.MatchIdText, "not-an-id"));

        cut.Markup.Should().Contain("not a fight");
        _ = _api.DidNotReceiveWithAnyArgs().GetAnalysisAsync(_id, CancellationToken.None);
    }
}

/// <summary>How a measured number is put into words.</summary>
public class StatFormatTests
{
    [Fact]
    public void Times_are_read_the_way_somebody_would_say_them()
    {
        StatFormat.Seconds(9.4).Should().Be("9.4s");
        StatFormat.Seconds(62).Should().Be("1m 2s");
        StatFormat.Seconds(0).Should().Be("0s");
    }

    [Fact]
    public void Shares_are_percentages_rather_than_fractions()
    {
        StatFormat.Percent(0.615).Should().Be("62%");
        StatFormat.Percent(0).Should().Be("0%");
        StatFormat.Percent(1).Should().Be("100%");
    }

    [Fact]
    public void Numbers_keep_one_decimal_where_it_matters_and_none_where_it_does_not()
    {
        StatFormat.Number(174).Should().Be("174");
        StatFormat.Number(3.24).Should().Be("3.2");
    }

    [Fact]
    public void An_empty_list_reads_as_nothing_rather_than_as_blank()
    {
        var metrics = new PlayerMetricsDto(
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            [], string.Empty, [], 0, 0, 0);

        var described = StatFormat.Describe(metrics);

        described.Should().Contain(s => s.Label == "Words they lean on" && s.Value == "—");
        described.Should().Contain(s => s.Label == "Longest word" && s.Value == "—");
    }

    [Fact]
    public void Every_trait_the_judge_scores_has_a_label_and_a_question_behind_it()
    {
        var traits = StatFormat.Traits(new PlayerAssessmentDto(
            "B2", "why", 0, [], 5, 5, 5, [], 5, 5, 5, 50, [],
            new EmotionProfileDto(0, 0, 0, 0, 0, 0), [], "q", [], "p", "e", "v", 5, 5, 5, 5, "b", "w", []));

        traits.Should().OnlyContain(t => !string.IsNullOrWhiteSpace(t.Label) && !string.IsNullOrWhiteSpace(t.Hint));
        traits.Should().OnlyContain(t => t.Score >= 0 && t.Score <= 10);
    }
}
