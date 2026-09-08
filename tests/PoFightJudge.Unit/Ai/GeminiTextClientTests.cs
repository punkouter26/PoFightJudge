using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PoFightJudge.Api.Features.Ai;
using PoFightJudge.Api.Features.Diagnostics;
using PoFightJudge.TestSupport;

namespace PoFightJudge.Unit.Ai;

public class GeminiTextClientTests
{
    private readonly FakeHttpHandler _http = new();
    private readonly AiLatencyTracker _latency = new();
    private readonly GeminiTextClient _sut;

    public GeminiTextClientTests()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => _http.CreateClient());
        _sut = new GeminiTextClient(factory, _latency, NullLogger<GeminiTextClient>.Instance);
    }

    private static GeminiTextRequest Request(JsonNode? schema = null, string? thinking = null) => new(
        new GeminiPrompt("You are the referee.", "Round 1: the dishwasher."),
        Model: "gemini-3.1-flash-lite",
        Operation: "round",
        ResponseSchema: schema,
        Temperature: 0.7,
        MaxOutputTokens: 256,
        ThinkingLevel: thinking);

    private static JsonObject Schema() => new()
    {
        ["type"] = "OBJECT",
        ["properties"] = new JsonObject { ["line"] = new JsonObject { ["type"] = "STRING" } },
        ["required"] = new JsonArray("line"),
    };

    [Fact]
    public async Task Generate_posts_the_split_prompt_with_structured_output_and_reads_the_first_non_thought_part()
    {
        _http.Enqueue(HttpStatusCode.OK, """
            {"candidates":[{"content":{"parts":[{"thought":true,"text":"hmm"},{"text":"{\"line\":\"No.\"}"}]}}],
             "usageMetadata":{"promptTokenCount":100,"candidatesTokenCount":5,"thoughtsTokenCount":2,"cachedContentTokenCount":40}}
            """);

        var text = await _sut.GenerateAsync(Request(Schema(), thinking: "minimal"));

        text.Should().Be("""{"line":"No."}""");
        var sent = _http.Requests.Should().ContainSingle().Which;
        sent.Uri!.ToString().Should().EndWith("v1beta/models/gemini-3.1-flash-lite:generateContent");
        var body = JsonNode.Parse(sent.Body!)!;
        body["systemInstruction"]!["parts"]![0]!["text"]!.GetValue<string>().Should().Be("You are the referee.");
        body["contents"]![0]!["role"]!.GetValue<string>().Should().Be("user");
        body["contents"]![0]!["parts"]![0]!["text"]!.GetValue<string>().Should().Be("Round 1: the dishwasher.");
        var config = body["generationConfig"]!;
        config["responseMimeType"]!.GetValue<string>().Should().Be("application/json");
        config["responseSchema"]!["properties"]!["line"].Should().NotBeNull();
        config["thinkingConfig"]!["thinkingLevel"]!.GetValue<string>().Should().Be("minimal");
        config["temperature"]!.GetValue<double>().Should().Be(0.7);
        config["maxOutputTokens"]!.GetValue<int>().Should().Be(256);
        body["model"].Should().BeNull("the model is in the path, not the body");

        _latency.Snapshot().Should().ContainSingle(s => string.Equals(s.Operation, "round", StringComparison.Ordinal));
        var usage = _latency.UsageSnapshot().Should().ContainSingle().Which;
        usage.PromptTokens.Should().Be(100);
        usage.OutputTokens.Should().Be(7, "thinking tokens bill as output");
        usage.CachedTokens.Should().Be(40);
    }

    [Fact]
    public async Task Without_a_schema_or_thinking_level_the_config_carries_neither()
    {
        _http.Enqueue(HttpStatusCode.OK, """{"candidates":[{"content":{"parts":[{"text":"plain"}]}}]}""");

        (await _sut.GenerateAsync(Request())).Should().Be("plain");

        var config = JsonNode.Parse(_http.Requests[0].Body!)!["generationConfig"]!;
        config["responseMimeType"].Should().BeNull();
        config["thinkingConfig"].Should().BeNull();
        _latency.UsageSnapshot().Should().BeEmpty("no usage block was returned");
    }

    [Fact]
    public async Task An_error_answer_becomes_an_exception_that_quotes_the_provider_message()
    {
        _http.Enqueue(HttpStatusCode.BadRequest, """{"error":{"code":400,"message":"API key not valid"}}""");

        var act = () => _sut.GenerateAsync(Request());

        var ex = (await act.Should().ThrowAsync<HttpRequestException>()).Which;
        ex.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ex.Message.Should().Contain("API key not valid").And.Contain("round");
    }

    [Fact]
    public async Task Stream_yields_fragments_in_order_records_ttft_and_the_trailing_usage()
    {
        const string sse = """
            : keep-alive

            data: {"candidates":[{"content":{"parts":[{"text":"{\"line\":\"We "}]}}]}

            data: {"candidates":[{"content":{"parts":[{"text":"talked.\"}"}]}}],"usageMetadata":{"promptTokenCount":9,"candidatesTokenCount":3}}

            data: [DONE]

            """;
        _http.Enqueue((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(sse, Encoding.UTF8, "text/event-stream") });

        var fragments = new List<string>();
        await foreach (var fragment in _sut.StreamAsync(Request(Schema())))
        {
            fragments.Add(fragment);
        }

        fragments.Should().Equal("{\"line\":\"We ", "talked.\"}");
        _http.Requests[0].Uri!.ToString().Should().EndWith(":streamGenerateContent?alt=sse");
        _latency.Snapshot().Select(s => s.Operation).Should().BeEquivalentTo(["round", "round.ttft"]);
        _latency.UsageSnapshot().Should().ContainSingle().Which.PromptTokens.Should().Be(9);
    }
}

public class SseParserTests
{
    private static async Task<List<string>> ParseAsync(string raw)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(raw));
        var events = new List<string>();
        await foreach (var data in SseParser.ReadDataAsync(stream))
        {
            events.Add(data);
        }

        return events;
    }

    [Fact]
    public async Task Joins_multi_line_data_skips_comments_and_other_fields_and_stops_at_done()
    {
        var events = await ParseAsync(": comment\r\nevent: message\r\ndata: one\r\ndata:two\r\n\r\ndata: three\n\ndata: [DONE]\n\ndata: after\n\n");

        events.Should().Equal("one\ntwo", "three");
    }

    [Fact]
    public async Task A_final_event_without_a_trailing_blank_line_is_still_delivered()
    {
        (await ParseAsync("data: {\"a\":1}")).Should().Equal("{\"a\":1}");
        (await ParseAsync(string.Empty)).Should().BeEmpty();
    }
}
