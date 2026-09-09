using PoFightJudge.Api.Features.Fighters;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Unit.Watch;

/// <summary>
/// A whole speaking history, counted. These are rates rather than totals, so the thing each test has to pin down is
/// that turning up more often does not move a number that is supposed to describe how somebody argues.
/// </summary>
public class CareerWordsTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 20, 0, 0, TimeSpan.Zero);

    private static SpokenDebate Said(string words, int dayOffset = 0) =>
        new("MAH", MatchId.New(), Start.AddDays(dayOffset), MatchMode.Watch, words);

    [Fact]
    public void Nothing_said_is_empty_rather_than_a_wall_of_zeroes()
    {
        var career = CareerWords.Compute([]);

        career.IsEmpty.Should().BeTrue();
        career.Should().Be(CareerWordsDto.Empty);
    }

    [Fact]
    public void A_debate_with_no_words_in_it_does_not_count_as_a_debate()
    {
        CareerWords.Compute([Said("   ")]).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Blame_is_the_accusing_kind_of_you_not_every_you()
    {
        var accusing = CareerWords.Compute([Said("You always do this and you never listen.")]);
        var friendly = CareerWords.Compute([Said("You and I agreed on this together, you know.")]);

        accusing.BlamePer100.Should().BeGreaterThan(0);
        friendly.BlamePer100.Should().Be(0, "second person on its own is not an accusation");
    }

    [Fact]
    public void Apologies_are_counted_apart_from_blame()
    {
        var career = CareerWords.Compute([Said("Sorry, that was my fault. You're right.")]);

        career.ApologyPer100.Should().BeGreaterThan(0);
        career.BlamePer100.Should().Be(0);
    }

    [Fact]
    public void Name_calling_is_not_swearing()
    {
        var cruel = CareerWords.Compute([Said("You are being lazy and selfish and childish.")]);

        cruel.NameCallingPer100.Should().BeGreaterThan(0);
        cruel.OffensivePercent.Should().Be(0, "somebody can be cruel without swearing, and the two are counted apart");
    }

    [Fact]
    public void Swearing_is_a_percentage_of_everything_said()
    {
        // Four words, one of them profanity.
        var career = CareerWords.Compute([Said("This is damn ridiculous")]);

        career.OffensivePercent.Should().Be(25);
    }

    [Fact]
    public void Receipts_are_the_specifics_not_the_generalities()
    {
        var specific = CareerWords.Compute([Said("You said Tuesday at 6 and it cost 40 dollars.")]);
        var vague = CareerWords.Compute([Said("You said it would be fine and it was not fine at all.")]);

        specific.ReceiptsPer100.Should().BeGreaterThan(vague.ReceiptsPer100);
    }

    [Fact]
    public void A_rate_does_not_climb_just_because_they_argued_twice()
    {
        var once = CareerWords.Compute([Said("You always do this.")]);
        var twice = CareerWords.Compute([Said("You always do this."), Said("You always do this.", 1)]);

        twice.BlamePer100.Should().Be(once.BlamePer100, "a rate describes how they argue, not how often they turn up");
        twice.Words.Should().Be(once.Words * 2, "the raw count still doubles; that is what makes it a total and not a rate");
    }

    [Fact]
    public void A_catchphrase_has_to_survive_more_than_one_argument()
    {
        var twice = CareerWords.Compute(
        [
            Said("at the end of the day you promised me", 0),
            Said("at the end of the day it is fine", 1),
        ]);

        var onlyOnce = CareerWords.Compute(
        [
            Said("at the end of the day at the end of the day", 0),
            Said("something else entirely happened here", 1),
        ]);

        twice.Catchphrases.Should().Contain(p => p.Contains("end of the day", StringComparison.Ordinal));
        onlyOnce.Catchphrases.Should().BeEmpty("said twice in one argument is a mood, not a habit");
    }

    [Fact]
    public void A_catchphrase_is_reported_at_its_longest()
    {
        var career = CareerWords.Compute(
        [
            Said("at the end of the day you promised", 0),
            Said("at the end of the day you promised", 1),
        ]);

        career.Catchphrases.Should().NotContain("at the end", "the longer phrase already says it");
    }

    [Fact]
    public void New_words_are_the_ones_they_had_never_used_before()
    {
        var career = CareerWords.Compute(
        [
            Said("the freezer was open", 0),
            Said("the freezer was open again yesterday", 1),
        ]);

        career.NewWordsLatest.Should().Be(2, "only \"again\" and \"yesterday\" had not been said before");
    }

    [Fact]
    public void A_first_debate_has_no_new_words_to_compare_against()
    {
        CareerWords.Compute([Said("the freezer was open")]).NewWordsLatest.Should().Be(0);
    }

    [Fact]
    public void Vocabulary_counts_each_word_once_however_often_it_was_said()
    {
        CareerWords.Compute([Said("no no no no")]).VocabularySize.Should().Be(1);
    }

    [Fact]
    public void The_grade_trend_runs_oldest_first_whatever_order_it_arrives_in()
    {
        var newestFirst = CareerWords.Compute(
        [
            Said("Consequently the arrangement necessitated considerable reconsideration.", 5),
            Said("I said no.", 0),
        ]);

        newestFirst.GradeTrend.Should().HaveCount(2);
        newestFirst.GradeTrend[0].Should().BeLessThan(newestFirst.GradeTrend[1], "the plain one was said first");
    }

    [Fact]
    public void A_question_with_an_edge_is_not_counted_as_curiosity()
    {
        var career = CareerWords.Compute([Said("Why did you do that? What time is it?")]);

        career.Challenges.Should().Be(1);
        career.GenuineQuestions.Should().Be(1);
    }

    [Fact]
    public void Time_orientation_splits_the_markers_it_found_not_every_word()
    {
        var backwards = CareerWords.Compute([Said("You said that yesterday and you told me last time.")]);

        (backwards.PastShare + backwards.PresentShare + backwards.FutureShare)
            .Should().BeApproximately(1.0, 0.01, "the three are shares of the markers, so they add up");
        backwards.PastShare.Should().BeGreaterThan(backwards.FutureShare);
    }

    [Fact]
    public void Language_with_no_tense_in_it_leans_nowhere()
    {
        var career = CareerWords.Compute([Said("Rubbish everywhere.")]);

        career.PastShare.Should().Be(0);
        career.PresentShare.Should().Be(0);
        career.FutureShare.Should().Be(0);
    }
}
