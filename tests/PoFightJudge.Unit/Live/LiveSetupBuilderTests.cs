using System.Text.Json;
using System.Text.Json.Nodes;
using PoFightJudge.Api.Features.Ai;
using PoFightJudge.Api.Features.Live;

namespace PoFightJudge.Unit.Live;

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
    public void The_model_is_named_the_way_the_live_api_names_models()
    {
        var setup = Build(new LiveSessionConfig("host the debate"));

        setup.GetProperty("model").GetString().Should().Be($"models/{Models.Live}");
    }

    [Fact]
    public void The_voice_and_the_audio_modality_sit_inside_the_generation_config()
    {
        var generation = Build(new LiveSessionConfig("host the debate")).GetProperty("generationConfig");

        generation.GetProperty("responseModalities")[0].GetString().Should().Be("AUDIO");
        generation.GetProperty("speechConfig").GetProperty("voiceConfig").GetProperty("prebuiltVoiceConfig")
            .GetProperty("voiceName").GetString().Should().Be(Models.Voice);
    }

    [Fact]
    public void A_session_voice_overrides_the_configured_default_so_each_host_sounds_different()
    {
        var setup = Build(new LiveSessionConfig("host the debate", Voice: "Charon"));

        setup.GetProperty("generationConfig").GetProperty("speechConfig").GetProperty("voiceConfig")
            .GetProperty("prebuiltVoiceConfig").GetProperty("voiceName").GetString().Should().Be("Charon");
    }

    [Fact]
    public void The_system_instruction_is_the_persona_that_was_asked_for()
    {
        var setup = Build(new LiveSessionConfig("You are a referee. Keep it moving."));

        setup.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString()
            .Should().Be("You are a referee. Keep it moving.");
    }

    [Fact]
    public void Both_sides_of_the_conversation_are_transcribed_and_the_window_is_compressed()
    {
        var setup = Build(new LiveSessionConfig("host the debate"));

        setup.TryGetProperty("inputAudioTranscription", out _).Should().BeTrue("the players' words are the analysis input");
        setup.TryGetProperty("outputAudioTranscription", out _).Should().BeTrue("the host's words are part of the show");
        setup.GetProperty("contextWindowCompression").TryGetProperty("slidingWindow", out _)
            .Should().BeTrue("a debate runs longer than the window holds");
    }

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

    [Fact]
    public void Resumption_is_asked_for_from_the_start_and_carries_the_handle_when_reconnecting()
    {
        var fresh = Build(new LiveSessionConfig("host the debate")).GetProperty("sessionResumption");
        fresh.TryGetProperty("handle", out _).Should().BeFalse("there is nothing to resume on a first connection");

        var again = Build(new LiveSessionConfig("host the debate", ResumptionHandle: "h-42")).GetProperty("sessionResumption");
        again.GetProperty("handle").GetString().Should().Be("h-42");
    }
}
