using PoMarriedFight.Api.Features.Diagnostics;

namespace PoMarriedFight.Unit.Ai;

public class AiLatencyTrackerTests
{
    [Fact]
    public void Percentiles_are_nearest_rank_over_a_rolling_window_per_operation()
    {
        var sut = new AiLatencyTracker();
        for (var i = 1; i <= 100; i++)
        {
            sut.Record("round", i);
        }

        sut.RecordTimeToFirstToken("round", 250);

        var round = sut.Snapshot().Single(s => string.Equals(s.Operation, "round", StringComparison.Ordinal));
        round.Count.Should().Be(100);
        round.P50Ms.Should().Be(50);
        round.P95Ms.Should().Be(95);
        round.LastMs.Should().Be(100);
        sut.Snapshot().Single(s => string.Equals(s.Operation, "round.ttft", StringComparison.Ordinal)).LastMs.Should().Be(250);
        sut.Snapshot().Select(s => s.Operation).Should().BeInAscendingOrder();
    }

    [Fact]
    public void The_window_keeps_only_the_newest_samples()
    {
        var sut = new AiLatencyTracker();
        for (var i = 0; i < AiLatencyTracker.WindowSize + 50; i++)
        {
            sut.Record("tts", i < 50 ? 10_000 : 1);
        }

        var tts = sut.Snapshot().Single();
        tts.Count.Should().Be(AiLatencyTracker.WindowSize);
        tts.P95Ms.Should().Be(1, "the 50 slow samples fell out of the window");
    }

    [Fact]
    public void The_token_ledger_accumulates_per_operation_and_derives_the_cache_hit_ratio()
    {
        var sut = new AiLatencyTracker();
        sut.RecordUsage("round", promptTokens: 1000, outputTokens: 50, cachedTokens: 800);
        sut.RecordUsage("round", promptTokens: 1000, outputTokens: 70, cachedTokens: 0);
        sut.RecordUsage("judge", promptTokens: 4000, outputTokens: 300, cachedTokens: 0);

        var usage = sut.UsageSnapshot();
        usage.Select(u => u.Operation).Should().Equal("judge", "round");
        var round = usage.Single(u => string.Equals(u.Operation, "round", StringComparison.Ordinal));
        round.Calls.Should().Be(2);
        round.PromptTokens.Should().Be(2000);
        round.OutputTokens.Should().Be(120);
        round.CacheHitRatio.Should().Be(0.4);
        round.AveragePromptTokens.Should().Be(1000);
        AiLatencyTracker.Percentile([], 0.5).Should().Be(0, "an empty window reads as zero rather than throwing");
    }
}
