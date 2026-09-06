using PoMarriedFight.Api.Features.Watch;

namespace PoMarriedFight.Unit.Watch;

public class ArgueScoreCalculatorTests
{
    [Fact]
    public void Every_value_is_capped_at_a_hundred_however_loaded_the_transcript_is()
    {
        var texts = Enumerable.Repeat(
            "fine! whatever! your fault! you always! you never! everyone knows! obviously! clearly! nobody! prove it!",
            20).ToList();

        var stats = ArgueScoreCalculator.Compute(texts, 10);

        foreach (var value in new[]
        {
            stats.PassiveAggressionIndex, stats.BlameMetric, stats.EmotionalVolatility, stats.HistoricalGrievanceRate,
            stats.DeflectionCoefficient, stats.VolumeScore, stats.WordCountDominance, stats.LexicalComplexity, stats.ApologyToInsultRatio,
        })
        {
            value.Should().BeInRange(0, 100);
        }

        stats.LogicalFallacyCount.Should().BeGreaterThan(0, "the fallacy count is a raw tally, not a percentage");
    }

    [Fact]
    public void An_empty_transcript_scores_zero_except_a_neutral_dominance()
    {
        var stats = ArgueScoreCalculator.Compute([], totalWordCount: 0);

        stats.VolumeScore.Should().Be(0);
        stats.LexicalComplexity.Should().Be(0);
        stats.LogicalFallacyCount.Should().Be(0);
        stats.WordCountDominance.Should().Be(50, "with nothing said by anyone, neither side dominated");
    }

    [Fact]
    public void Dominance_is_a_share_of_the_whole_match_not_of_the_speaker()
    {
        var mine = new[] { "one two three four five six seven eight nine ten" };

        ArgueScoreCalculator.Compute(mine, totalWordCount: 20).WordCountDominance.Should().Be(50);
        ArgueScoreCalculator.Compute(mine, totalWordCount: 10).WordCountDominance.Should().Be(100);
        ArgueScoreCalculator.WordCount("one two  three").Should().Be(3);
    }

    [Fact]
    public void The_markers_actually_move_the_metrics_they_name()
    {
        var passive = ArgueScoreCalculator.Compute(["Fine. Whatever. I guess."], 3);
        var grievance = ArgueScoreCalculator.Compute(["You always do this and you never listen."], 8);
        var blame = ArgueScoreCalculator.Compute(["That is your fault and you made it worse."], 8);
        var apologetic = ArgueScoreCalculator.Compute(["I am sorry, please forgive me."], 6);
        var neutral = ArgueScoreCalculator.Compute(["The dishwasher finished its cycle at noon."], 7);

        passive.PassiveAggressionIndex.Should().BeGreaterThan(neutral.PassiveAggressionIndex);
        grievance.HistoricalGrievanceRate.Should().BeGreaterThan(neutral.HistoricalGrievanceRate);
        blame.BlameMetric.Should().BeGreaterThan(neutral.BlameMetric);
        apologetic.ApologyToInsultRatio.Should().BeGreaterThan(neutral.ApologyToInsultRatio);
        ArgueScoreCalculator.Compute(["Stop it! Stop it! Stop it!"], 6).EmotionalVolatility
            .Should().BeGreaterThan(neutral.EmotionalVolatility, "exclamation marks are the volatility signal");
        ArgueScoreCalculator.Compute(["Why? Where? When?"], 3).DeflectionCoefficient
            .Should().BeGreaterThan(neutral.DeflectionCoefficient, "questions are the deflection signal");
    }

    [Fact]
    public void The_same_transcript_always_scores_the_same()
    {
        var texts = new[] { "You always do this.", "Fine, whatever." };

        ArgueScoreCalculator.Compute(texts, 20).Should().Be(ArgueScoreCalculator.Compute(texts, 20));
    }
}
