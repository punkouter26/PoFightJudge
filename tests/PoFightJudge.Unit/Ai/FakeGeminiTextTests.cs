using System.Text.Json.Nodes;
using PoFightJudge.Api.Features.Ai;
using PoFightJudge.Api.Features.Ai.Fakes;
using PoFightJudge.Api.Features.Diagnostics;

namespace PoFightJudge.Unit.Ai;

public class FakeGeminiTextTests
{
    private static readonly JsonObject PersonaSchema = new()
    {
        ["type"] = "OBJECT",
        ["properties"] = new JsonObject
        {
            ["name"] = new JsonObject { ["type"] = "STRING" },
            ["age"] = new JsonObject { ["type"] = "INTEGER", ["minimum"] = 18, ["maximum"] = 120 },
            ["patience"] = new JsonObject { ["type"] = "INTEGER" },
            ["stressResponse"] = new JsonObject { ["type"] = "STRING", ["enum"] = new JsonArray("Fight", "Flight", "Freeze", "Fawn") },
            ["likes"] = new JsonObject { ["type"] = "ARRAY", ["items"] = new JsonObject { ["type"] = "STRING" }, ["minItems"] = 2, ["maxItems"] = 3 },
            ["sarcastic"] = new JsonObject { ["type"] = "BOOLEAN" },
            ["voice"] = new JsonObject { ["type"] = "OBJECT", ["properties"] = new JsonObject { ["pitch"] = new JsonObject { ["type"] = "NUMBER", ["minimum"] = 0.5, ["maximum"] = 2 } } },
        },
    };

    private static GeminiTextRequest Request(string user, JsonNode? schema) => new(new GeminiPrompt("sys", user), "fake-model", "profile", schema);

    [Fact]
    public async Task A_schema_produces_conforming_json_deterministically_for_the_same_prompt()
    {
        var tracker = new AiLatencyTracker();
        var sut = new FakeGeminiText(tracker, TimeSpan.Zero);

        var first = JsonNode.Parse(await sut.GenerateAsync(Request("make a wife", PersonaSchema)))!;
        var again = JsonNode.Parse(await sut.GenerateAsync(Request("make a wife", PersonaSchema)))!;
        var other = JsonNode.Parse(await sut.GenerateAsync(Request("make a husband", PersonaSchema)))!;

        first.ToJsonString().Should().Be(again.ToJsonString(), "the same prompt seeds the same faker");
        first.ToJsonString().Should().NotBe(other.ToJsonString());
        first["name"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
        first["age"]!.GetValue<int>().Should().BeInRange(18, 120);
        first["patience"]!.GetValue<int>().Should().BeInRange(0, 100, "integers default to the slider range");
        first["stressResponse"]!.GetValue<string>().Should().BeOneOf("Fight", "Flight", "Freeze", "Fawn");
        first["likes"]!.AsArray().Count.Should().BeInRange(2, 3);
        first["voice"]!["pitch"]!.GetValue<double>().Should().BeInRange(0.5, 2);
        sut.IsFake.Should().BeTrue();
        tracker.UsageSnapshot().Should().ContainSingle(u => string.Equals(u.Operation, "profile", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Streaming_delivers_the_same_text_in_pieces_and_without_a_schema_it_is_a_spoken_line()
    {
        var sut = new FakeGeminiText(new AiLatencyTracker(), TimeSpan.Zero);
        var request = Request("round 2", schema: null);

        var whole = await sut.GenerateAsync(request);
        var pieces = new List<string>();
        await foreach (var piece in sut.StreamAsync(request))
        {
            pieces.Add(piece);
        }

        pieces.Should().HaveCountGreaterThan(1);
        string.Concat(pieces).Should().Be(whole);
        whole.Should().EndWith(".");
    }
}
