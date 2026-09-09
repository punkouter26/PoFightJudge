using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PoFightJudge.Api.Features.Ai;
using PoFightJudge.Api.Features.Diagnostics;
using PoFightJudge.Api.Features.Watch;
using PoFightJudge.TestSupport;

namespace PoFightJudge.Unit.Ai;

/// <summary>
/// A local model behind the same seam Gemini sits behind.
/// </summary>
/// <remarks>
/// A development session with no key gets FakeGeminiText, which reads the response schema and synthesizes something
/// that satisfies it. That is right for the test tiers — deterministic, free, offline — and poor for actually
/// looking at the app, because the argument is four canned lines on a loop.
///
/// IGeminiText is a two-method transport, so a local runtime drops straight in. Ollama's /api/chat takes a JSON
/// schema in "format", which is exactly what GeminiTextRequest.ResponseSchema already carries, and streams NDJSON,
/// which is what StreamAsync already wants. Never in Production: registration is guarded the same way the fakes
/// are.
/// </remarks>
public class OllamaTextClientTests
{
    private static readonly GeminiPrompt Prompt = new("You write one line of dialogue.", "PARTICIPANTS: ...");

    // Held as a field rather than made per call: it is disposable, and the suite already keeps its fake handler
    // this way everywhere else.
    private readonly FakeHttpHandler _http = new();
    private readonly AiLatencyTracker _latency = new();

    private static GeminiTextRequest Round() => new(
        Prompt,
        "qwen3:8b",
        WatchAi.RoundOperation,
        WatchAi.ArgumentSchema,
        Temperature: 0.9,
        MaxOutputTokens: 512,
        ThinkingLevel: "minimal");

    [Fact]
    public void The_prompt_keeps_its_two_halves_as_two_messages()
    {
        var body = JsonNode.Parse(OllamaTextClient.BuildPayload(Round(), stream: false))!.AsObject();

        var messages = body["messages"]!.AsArray();
        messages.Should().HaveCount(2);
        messages[0]!["role"]!.GetValue<string>().Should().Be("system");
        messages[0]!["content"]!.GetValue<string>().Should().Be(Prompt.System);
        messages[1]!["role"]!.GetValue<string>().Should().Be("user");
        messages[1]!["content"]!.GetValue<string>().Should().Be(Prompt.User);
    }

    /// <summary>
    /// The whole point of the split is a stable prefix. Ollama's own prompt cache keys on the leading tokens the
    /// same way, so merging the halves would cost here too.
    /// </summary>
    [Fact]
    public void A_prompt_with_no_system_half_sends_one_message_rather_than_an_empty_one()
    {
        var request = Round() with { Prompt = new GeminiPrompt(string.Empty, "just this") };

        var messages = JsonNode.Parse(OllamaTextClient.BuildPayload(request, stream: false))!["messages"]!.AsArray();

        messages.Should().ContainSingle();
        messages[0]!["role"]!.GetValue<string>().Should().Be("user");
    }

    [Fact]
    public void The_response_schema_travels_as_the_format_because_that_is_what_it_already_is()
    {
        var body = JsonNode.Parse(OllamaTextClient.BuildPayload(Round(), stream: false))!.AsObject();

        var format = body["format"]!.AsObject();
        format["type"]!.GetValue<string>().Should().Be("object");
        format["properties"]!.AsObject().Should().ContainKey("line").And.ContainKey("mood");
    }

    [Fact]
    public void A_request_with_no_schema_asks_for_no_format_at_all()
    {
        var request = Round() with { ResponseSchema = null };

        JsonNode.Parse(OllamaTextClient.BuildPayload(request, stream: false))!.AsObject()
            .Should().NotContainKey("format", "an unconstrained call is prose, and \"json\" would refuse it");
    }

    [Fact]
    public void The_ceiling_and_the_temperature_go_where_this_runtime_keeps_them()
    {
        var options = JsonNode.Parse(OllamaTextClient.BuildPayload(Round(), stream: false))!["options"]!.AsObject();

        options["temperature"]!.GetValue<double>().Should().Be(0.9);
        options["num_predict"]!.GetValue<int>().Should().Be(512);
    }

    [Fact]
    public async Task A_line_comes_back_out_of_the_message_content()
    {
        var sut = Client("""{"model":"qwen3:8b","message":{"role":"assistant","content":"{\"line\":\"You never listen.\",\"mood\":\"angry\"}"},"done":true}""");

        var text = await sut.GenerateAsync(Round());

        text.Should().Be("""{"line":"You never listen.","mood":"angry"}""");
        WatchAi.ParseArgument(text).Text.Should().Be("You never listen.", "what comes back has to survive the parser the game already has");
    }

    [Fact]
    public async Task A_stream_yields_each_fragment_and_stops_at_done()
    {
        var sut = Client(
            """
            {"message":{"role":"assistant","content":"You "},"done":false}
            {"message":{"role":"assistant","content":"never "},"done":false}
            {"message":{"role":"assistant","content":"listen."},"done":false}
            {"message":{"role":"assistant","content":""},"done":true,"prompt_eval_count":120,"eval_count":9}
            """);

        var fragments = new List<string>();
        await foreach (var fragment in sut.StreamAsync(Round()))
        {
            fragments.Add(fragment);
        }

        fragments.Should().Equal("You ", "never ", "listen.");
        string.Concat(fragments).Should().Be("You never listen.");
    }

    /// <summary>
    /// The ledger is what says a prompt change helped, and it has to read the same whichever runtime answered.
    /// Ollama has no context cache to report, so the cached share is honestly zero rather than absent.
    /// </summary>
    [Fact]
    public async Task What_it_cost_lands_on_the_same_ledger_gemini_writes_to()
    {
        var sut = Client("""{"message":{"content":"hi"},"done":true,"prompt_eval_count":120,"eval_count":9}""");

        await sut.GenerateAsync(Round());

        var usage = _latency.UsageSnapshot().Single();
        usage.Operation.Should().Be(WatchAi.RoundOperation);
        usage.PromptTokens.Should().Be(120);
        usage.OutputTokens.Should().Be(9);
        usage.CachedTokens.Should().Be(0);
        _latency.Snapshot().Should().ContainSingle(s => string.Equals(s.Operation, WatchAi.RoundOperation, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_runtime_that_is_not_running_says_so_rather_than_returning_nothing()
    {
        var sut = Client(string.Empty, HttpStatusCode.NotFound);

        var generate = async () => await sut.GenerateAsync(Round());

        await generate.Should().ThrowAsync<HttpRequestException>().WithMessage("*Ollama*");
    }

    /// <summary>It is a real model, not a stand-in, and the banner must not claim otherwise.</summary>
    [Fact]
    public void It_does_not_pretend_to_be_a_fake()
    {
        Client("{}").IsFake.Should().BeFalse();
    }

    /// <summary>The same fake handler every other HTTP client test here uses; it owns the one allowed HttpClient.</summary>
    private OllamaTextClient Client(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        _http.Enqueue(status, body, "application/x-ndjson");
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => _http.CreateClient("http://localhost:11434/"));
        return new OllamaTextClient(factory, _latency, NullLogger<OllamaTextClient>.Instance);
    }
}

/// <summary>Which runtime answers, and the one place it must never be a local one.</summary>
public class OllamaRegistrationTests
{
    [Fact]
    public void A_local_runtime_is_never_what_production_talks_to()
    {
        OllamaOptions.ShouldUse(new OllamaOptions { Enabled = true }, isProduction: true)
            .Should().BeFalse("the same rule the fakes are held to: nothing that is not the real service in Production");

        OllamaOptions.ShouldUse(new OllamaOptions { Enabled = true }, isProduction: false).Should().BeTrue();
        OllamaOptions.ShouldUse(new OllamaOptions { Enabled = false }, isProduction: false).Should().BeFalse();
    }

    [Fact]
    public void It_answers_on_the_address_ollama_actually_listens_on()
    {
        new OllamaOptions().Endpoint.Should().Be(new Uri("http://localhost:11434/"));
        new OllamaOptions().Model.Should().NotBeNullOrWhiteSpace();
    }
}
