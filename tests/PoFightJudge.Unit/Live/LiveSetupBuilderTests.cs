using System.Text.Json;
using System.Text.Json.Nodes;
using PoFightJudge.Api.Features.Ai;
using PoFightJudge.Api.Features.Live;

namespace PoFightJudge.Unit.Live;

/// <summary>
/// The setup frame. Its shape was checked against the Live API reference on 2026-09-06: response modality and voice
/// live inside <c>generationConfig</c>, everything else sits directly on <c>setup</c>.
/// </summary>
/// <summary>
/// The setup frame. Its shape was checked against the Live API reference on 2026-09-06: response modality and voice
/// live inside <c>generationConfig</c>, everything else sits directly on <c>setup</c>.
/// </summary>
public class LiveSetupBuilderTests
{
    private static readonly GeminiModelOptions Models = GeminiModelOptions.Defaults;

    private static JsonElement Build(LiveSessionConfig config) =>
        JsonDocument.Parse(LiveSetupBuilder.Build(Models, config)).RootElement.GetProperty("setup");

    [Fact]
    public void Search_is_always_offered_and_our_own_tools_only_when_there_are_some()
    {
        var without = Build(new LiveSessionConfig("host the debate")).GetProperty("tools");
        without.GetArrayLength().Should().Be(1);
        without[0].TryGetProperty("googleSearch", out _).Should().BeTrue("the host checks claims out loud");

        JsonArray declarations = [new JsonObject { ["name"] = "call_verdict" }];
        var with = Build(new LiveSessionConfig("host the debate", FunctionDeclarations: declarations)).GetProperty("tools");
        with.GetArrayLength().Should().Be(2);
        with[1].GetProperty("functionDeclarations")[0].GetProperty("name").GetString().Should().Be("call_verdict");
    }
}
