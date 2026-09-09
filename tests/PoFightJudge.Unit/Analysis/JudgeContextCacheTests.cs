using System.Text.Json;
using PoFightJudge.Api.Features.Ai;
using PoFightJudge.Api.Features.Analysis;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Unit.Analysis;

/// <summary>
/// The judge's shared prefix, cached explicitly rather than hoped for.
/// </summary>
/// <remarks>
/// The two assessments were sent one after the other so the second would find the instructions, the recording and
/// the session data still warm in the provider's implicit cache. That is best-effort by definition — the discount
/// is not promised and nothing here could tell whether it landed — and paying for it cost the assessments their
/// concurrency, on the slowest leg of a 38.1 s analysis. Cached explicitly, the prefix is uploaded once under a
/// name, both assessments reference it, and they go out together.
///
/// The cache is not load-bearing: it needs a minimum token count that a very short fight may not reach, so a
/// refusal falls back to the inline requests this replaced, unchanged.
/// </remarks>
public class JudgeContextCacheTests
{
    private static readonly GeminiModelOptions Models = GeminiModelOptions.Defaults;

    private static JudgeRequest Request() => new(
        "the thermostat",
        "AL",
        "SM",
        "files/abc",
        "audio/wav",
        new MappedTranscript(
            [
                new MappedWord("you", Speaker.Player1, 1, 1.2),
                new MappedWord("never", Speaker.Player1, 1.2, 1.5),
                new MappedWord("rubbish", Speaker.Player2, 2.5, 3),
            ],
            Flagged: false,
            Note: string.Empty),
        [new TurnDto(MatchId.New(), 0, Speaker.Player1, TurnKind.Talk, string.Empty) { StartSeconds = 1, EndSeconds = 2 }],
        Metrics(words: 3, talkShare: 0.6),
        Metrics(words: 1, talkShare: 0.4),
        HostVerdict: "AL took it.");

    private static PlayerMetricsDto Metrics(int words, double talkShare) => new(
        TalkSeconds: 2, TalkShare: talkShare, Turns: 1, AverageTurnSeconds: 2, LongestTurnSeconds: 2,
        Words: words, WordsPerMinute: 90, Pauses: 0, MeanPauseSeconds: 0, LongestPauseSeconds: 0,
        InterruptionsMade: 0, InterruptionsReceived: 0, OverlapSeconds: 0, HostInterrupts: 0,
        FillersPer100: 0, Hedges: 0, Absolutes: 0, Questions: 0, TypeTokenRatio: 1, MeanWordLength: 4.5,
        SyllablesPerWord: 1.2, MeanSentenceLength: 3, FleschReadingEase: 70, FleschKincaidGrade: 6,
        TopWords: [], LongestWord: "listen", RepeatedPhrases: [], Profanity: 0,
        FirstPersonRatio: 0, SecondPersonRatio: 0.3);

    [Fact]
    public void The_cache_carries_the_prefix_both_assessments_share_and_nothing_that_differs()
    {
        var body = JsonDocument.Parse(GeminiJudgeClient.BuildCacheRequest(Request(), Models, TimeSpan.FromMinutes(10))).RootElement;

        body.GetProperty("model").GetString().Should().Be($"models/{Models.Judge}", "the cache is bound to the model that will read it");
        body.GetProperty("ttl").GetString().Should().Be("600s");

        var parts = body.GetProperty("contents")[0].GetProperty("parts");
        parts.GetArrayLength().Should().Be(3, "the instructions, the recording and the session data — the task is what differs");
        parts[0].GetProperty("text").GetString().Should().Be(GeminiJudgeClient.Instructions);
        parts[1].GetProperty("fileData").GetProperty("fileUri").GetString().Should().Be("files/abc");
        parts[2].GetProperty("text").GetString().Should().Contain("TRANSCRIPT");
        parts.EnumerateArray().Select(p => p.GetRawText()).Should()
            .NotContain(raw => raw.Contains("TASK:", StringComparison.Ordinal), "the task is what the two calls differ by");
    }

    [Fact]
    public void A_cached_assessment_sends_only_the_task_and_names_the_cache()
    {
        var body = JsonDocument.Parse(
            GeminiJudgeClient.BuildAssessmentRequest(Request(), first: true, Models, "cachedContents/abc123")).RootElement;

        body.GetProperty("cachedContent").GetString().Should().Be("cachedContents/abc123");

        var parts = body.GetProperty("contents")[0].GetProperty("parts");
        parts.GetArrayLength().Should().Be(1, "everything else is in the cache; re-sending it is what the cache is for avoiding");
        parts[0].GetProperty("text").GetString().Should().Contain("TASK: assess AL");
        parts.EnumerateArray().Select(p => p.GetRawText()).Should()
            .NotContain(raw => raw.Contains("fileData", StringComparison.Ordinal), "the recording is cached, not re-uploaded");
    }

    /// <summary>The discount and the ceiling are properties of the call, not of the prefix, so they still travel.</summary>
    [Fact]
    public void A_cached_assessment_keeps_the_tier_the_schema_and_the_ceiling()
    {
        var body = JsonDocument.Parse(
            GeminiJudgeClient.BuildAssessmentRequest(Request(), first: true, Models, "cachedContents/abc123")).RootElement;

        body.GetProperty("service_tier").GetString().Should().Be(Models.JudgeServiceTier);
        body.GetProperty("generationConfig").GetProperty("maxOutputTokens").GetInt32().Should().Be(GeminiJudgeClient.MaxOutputTokens);
        body.GetProperty("generationConfig").GetProperty("responseSchema").GetProperty("type").GetString().Should().Be("OBJECT");
        body.GetProperty("generationConfig").GetProperty("thinkingConfig").GetProperty("thinkingLevel").GetString()
            .Should().Be(Models.JudgeThinkingLevel);
    }

    /// <summary>The escalation off a busy flex tier has to keep working on a request that names a cache.</summary>
    [Fact]
    public void A_cached_request_can_still_be_escalated_to_the_standard_tier()
    {
        var discounted = GeminiJudgeClient.BuildAssessmentRequest(Request(), first: true, Models, "cachedContents/abc123");

        var standard = GeminiJudgeClient.WithoutServiceTier(discounted);

        standard.Should().NotBeNull();
        var body = JsonDocument.Parse(standard!).RootElement;
        body.TryGetProperty("service_tier", out _).Should().BeFalse();
        body.GetProperty("cachedContent").GetString().Should().Be("cachedContents/abc123", "the prefix is still cached at full price");
    }

    /// <summary>Without a cache name the request is exactly the one this replaced — the fallback has to be the old path.</summary>
    [Fact]
    public void With_no_cache_the_request_is_the_whole_thing_inline_as_before()
    {
        var uncached = GeminiJudgeClient.BuildAssessmentRequest(Request(), first: true, Models, cacheName: null);

        var body = JsonDocument.Parse(uncached).RootElement;
        body.TryGetProperty("cachedContent", out _).Should().BeFalse();
        body.GetProperty("contents")[0].GetProperty("parts").GetArrayLength().Should().Be(4);
        uncached.Should().Be(GeminiJudgeClient.BuildAssessmentRequest(Request(), first: true, Models),
            "the no-cache overload is the same request, not a second spelling of it");
    }

    [Fact]
    public void The_name_of_a_created_cache_is_read_off_the_answer()
    {
        GeminiJudgeClient.ParseCacheName("""{"name":"cachedContents/xyz","usageMetadata":{"totalTokenCount":9001}}""")
            .Should().Be("cachedContents/xyz");
    }

    /// <summary>
    /// A cache the provider refused, or answered without a name, is not a cache. Returning null here is what sends
    /// the judge back to the inline path rather than to a request naming something that does not exist.
    /// </summary>
    [Fact]
    public void An_answer_with_no_name_in_it_is_not_a_cache()
    {
        GeminiJudgeClient.ParseCacheName("""{"error":{"code":400,"message":"Cached content is too small"}}""").Should().BeNull();
        GeminiJudgeClient.ParseCacheName("{}").Should().BeNull();
        GeminiJudgeClient.ParseCacheName("not json at all").Should().BeNull();
        GeminiJudgeClient.ParseCacheName("""{"name":""}""").Should().BeNull();
    }
}
