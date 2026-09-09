using System.Text.Json.Nodes;
using PoFightJudge.Api.Features.Ai;
using PoFightJudge.Api.Features.Analysis;
using PoFightJudge.Api.Features.Diagnostics;
using PoFightJudge.Api.Features.Watch;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Unit.Diagnostics;

/// <summary>
/// What the token ledger is read off, and who fills it in.
/// </summary>
/// <remarks>
/// The whole shape of the round prompt and of the judge's three calls is a bet that a shared prefix is being served
/// from the provider's cache — RoundPromptBuilder's own comment says the failure is invisible and the only symptom
/// is the bill. <c>cachedContentTokenCount</c> is the number that says whether the bet is paying, and until now the
/// judge, which sends by far the largest prompt in the app, reported none of it: it builds its own payloads and
/// never went near the tracker.
/// </remarks>
public class AiUsageTests
{
    [Fact]
    public void The_ledger_reads_the_counts_the_provider_returns()
    {
        var usage = JsonNode.Parse("""
            {"promptTokenCount":12000,"candidatesTokenCount":900,"thoughtsTokenCount":100,"cachedContentTokenCount":11000}
            """)!.AsObject();

        var read = GeminiUsage.Read(usage);

        read.PromptTokens.Should().Be(12000);
        read.CachedTokens.Should().Be(11000);
        read.OutputTokens.Should().Be(1000, "thinking tokens bill as output and are invisible in the text");
    }

    [Fact]
    public void A_response_that_reported_nothing_is_zeroes_rather_than_a_throw()
    {
        GeminiUsage.Read(null).Should().Be(default(GeminiUsage));
        GeminiUsage.Read(JsonNode.Parse("{}")!.AsObject()).Should().Be(default(GeminiUsage));
        GeminiUsage.Read(JsonNode.Parse("""{"promptTokenCount":"lots"}""")!.AsObject()).PromptTokens.Should().Be(0);
    }

    /// <summary>The scoreboard for cache-friendly prompt ordering, and what it reads when nothing has been sent.</summary>
    [Fact]
    public void The_cached_share_is_the_number_a_prompt_layout_is_judged_on()
    {
        new AiUsageDto("round", 4, PromptTokens: 4000, OutputTokens: 400, CachedTokens: 3000)
            .CacheHitRatio.Should().BeApproximately(0.75, 0.001);

        new AiUsageDto("round", 0, 0, 0, 0).CacheHitRatio.Should().Be(0, "nothing sent is not a miss");
    }

    /// <summary>
    /// Two different judges. The WATCH one rules on six lines of dialogue; the analysis one reads a whole recording.
    /// Sharing an operation name would average a 1 s call together with a 30 s one and describe neither.
    /// </summary>
    [Fact]
    public void The_two_judges_are_counted_apart()
    {
        var names = new[] { WatchAi.JudgeOperation, GeminiJudgeClient.AssessOperation, GeminiJudgeClient.RuleOperation };

        names.Should().OnlyHaveUniqueItems();
        GeminiJudgeClient.AssessOperation.Should().NotBe(WatchAi.JudgeOperation);
    }

    [Fact]
    public void Every_recorded_operation_shows_up_on_the_readout_with_its_own_ledger()
    {
        var tracker = new AiLatencyTracker();

        tracker.Record(GeminiJudgeClient.AssessOperation, 12_000);
        tracker.RecordUsage(GeminiJudgeClient.AssessOperation, promptTokens: 12000, outputTokens: 900, cachedTokens: 11000);
        tracker.Record(GeminiJudgeClient.RuleOperation, 800);
        tracker.RecordUsage(GeminiJudgeClient.RuleOperation, promptTokens: 900, outputTokens: 200, cachedTokens: 0);

        var usage = tracker.UsageSnapshot();
        usage.Should().HaveCount(2);
        usage.Single(u => string.Equals(u.Operation, GeminiJudgeClient.AssessOperation, StringComparison.Ordinal))
            .CacheHitRatio.Should().BeGreaterThan(0.9, "the assessment prefix is what the cache is for");
        usage.Single(u => string.Equals(u.Operation, GeminiJudgeClient.RuleOperation, StringComparison.Ordinal))
            .CacheHitRatio.Should().Be(0, "the ruling is text-only and shares nothing worth caching");

        tracker.Snapshot().Select(s => s.Operation).Should()
            .Equal([GeminiJudgeClient.AssessOperation, GeminiJudgeClient.RuleOperation]);
    }
}
