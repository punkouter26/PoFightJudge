using PoFightJudge.Api.Features.Watch;

namespace PoFightJudge.Unit.Watch;

public class ArgueScoreCalculatorTests
{
    [Fact]
    public void Every_value_is_capped_at_a_hundred_however_loaded_the_transcript_is_and_the_same_transcript_always_scores_the_same()
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

        // The same transcript always scores the same.
        var plain = new[] { "You always do this.", "Fine, whatever." };

        ArgueScoreCalculator.Compute(plain, 20).Should().Be(ArgueScoreCalculator.Compute(plain, 20));
    }
}
