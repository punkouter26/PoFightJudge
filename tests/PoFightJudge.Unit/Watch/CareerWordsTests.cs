using PoFightJudge.Api.Features.Fighters;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Unit.Watch;

/// <summary>
/// A whole speaking history, counted. These are rates rather than totals, asserting that
/// turning up more often does not distort how somebody argues.
/// </summary>
public class CareerWordsTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 20, 0, 0, TimeSpan.Zero);

    private static SpokenDebate Said(string words, int dayOffset = 0) =>
        new("MAH", MatchId.New(), Start.AddDays(dayOffset), MatchMode.Watch, words);

    [Fact]
    public void Empty_and_whitespace_debates_are_empty()
    {
        var empty = CareerWords.Compute([]);
        empty.IsEmpty.Should().BeTrue();
        empty.Should().Be(CareerWordsDto.Empty);

        CareerWords.Compute([Said("   ")]).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Sentiment_attribution_and_tone_metrics_behave_as_rates()
    {
        var accusing = CareerWords.Compute([Said("You always do this and you never listen.")]);
        var friendly = CareerWords.Compute([Said("You and I agreed on this together, you know.")]);
        accusing.BlamePer100.Should().BeGreaterThan(0);
        friendly.BlamePer100.Should().Be(0, "second person on its own is not an accusation");

        var apology = CareerWords.Compute([Said("Sorry, that was my fault. You're right.")]);
        apology.ApologyPer100.Should().BeGreaterThan(0);
        apology.BlamePer100.Should().Be(0);

        var cruel = CareerWords.Compute([Said("You are being lazy and selfish and childish.")]);
        cruel.NameCallingPer100.Should().BeGreaterThan(0);
        cruel.OffensivePercent.Should().Be(0, "somebody can be cruel without swearing, and the two are counted apart");

        var swearing = CareerWords.Compute([Said("This is damn ridiculous")]);
        swearing.OffensivePercent.Should().Be(25);

        var once = CareerWords.Compute([Said("You always do this.")]);
        var twice = CareerWords.Compute([Said("You always do this."), Said("You always do this.", 1)]);
        twice.BlamePer100.Should().Be(once.BlamePer100, "a rate describes how they argue, not how often they turn up");
        twice.Words.Should().Be(once.Words * 2, "the raw count still doubles; that is what makes it a total and not a rate");
    }

    [Fact]
    public void Evidence_receipts_and_vocabulary_metrics()
    {
        var specific = CareerWords.Compute([Said("You said Tuesday at 6 and it cost 40 dollars.")]);
        var vague = CareerWords.Compute([Said("You said it would be fine and it was not fine at all.")]);
        specific.ReceiptsPer100.Should().BeGreaterThan(vague.ReceiptsPer100);

        CareerWords.Compute([Said("no no no no")]).VocabularySize.Should().Be(1);
        CareerWords.Compute([Said("the freezer was open")]).NewWordsLatest.Should().Be(0);

        var newWords = CareerWords.Compute(
        [
            Said("the freezer was open", 0),
            Said("the freezer was open again yesterday", 1),
        ]);
        newWords.NewWordsLatest.Should().Be(2, "only \"again\" and \"yesterday\" had not been said before");
    }

    [Fact]
    public void Catchphrases_require_recurrence_across_debates_and_longest_match()
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

        var longest = CareerWords.Compute(
        [
            Said("at the end of the day you promised", 0),
            Said("at the end of the day you promised", 1),
        ]);
        longest.Catchphrases.Should().NotContain("at the end", "the longer phrase already says it");
    }

    [Fact]
    public void Readability_grade_trends_questions_and_temporal_orientation()
    {
        var newestFirst = CareerWords.Compute(
        [
            Said("Consequently the arrangement necessitated considerable reconsideration.", 5),
            Said("I said no.", 0),
        ]);
        newestFirst.GradeTrend.Should().HaveCount(2);
        newestFirst.GradeTrend[0].Should().BeLessThan(newestFirst.GradeTrend[1], "the plain one was said first");

        var questions = CareerWords.Compute([Said("Why did you do that? What time is it?")]);
        questions.Challenges.Should().Be(1);
        questions.GenuineQuestions.Should().Be(1);

        var backwards = CareerWords.Compute([Said("You said that yesterday and you told me last time.")]);
        (backwards.PastShare + backwards.PresentShare + backwards.FutureShare)
            .Should().BeApproximately(1.0, 0.01, "the three are shares of the markers, so they add up");
        backwards.PastShare.Should().BeGreaterThan(backwards.FutureShare);

        var noTense = CareerWords.Compute([Said("Rubbish everywhere.")]);
        noTense.PastShare.Should().Be(0);
        noTense.PresentShare.Should().Be(0);
        noTense.FutureShare.Should().Be(0);
    }
}
