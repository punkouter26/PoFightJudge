using Microsoft.Extensions.Configuration;
using PoMarriedFight.Api.Features.Ai;
using PoMarriedFight.Shared.Configuration;

namespace PoMarriedFight.Unit.Ai;

/// <summary>Model tiering is the main cost knob, so it has to be overridable — and the defaults are the ids verified on 2026-09-06.</summary>
public class GeminiModelOptionsTests
{
    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void Defaults_are_the_verified_ids_with_rounds_on_a_cheaper_tier_than_the_judge()
    {
        var d = GeminiModelOptions.Defaults;

        d.Round.Should().Be("gemini-3.1-flash-lite");
        d.Judge.Should().Be("gemini-3.7-flash");
        d.Profile.Should().Be("gemini-3.7-flash");
        d.Tts.Should().Be("gemini-3.1-flash-tts-preview");
        d.Live.Should().Be("gemini-3.1-flash-live-preview");
        d.Transcribe.Should().Be("gemini-3.5-transcribe");
        d.Voice.Should().Be("Puck");
        d.JudgeThinkingLevel.Should().Be("low");
        d.JudgeServiceTier.Should().Be("flex");
        d.Round.Should().NotBe(d.Judge, "dialogue is short and frequent; the judge reads a whole transcript once");
    }

    [Fact]
    public void Each_operation_can_be_pointed_at_a_different_model_and_blanks_keep_the_default()
    {
        var models = GeminiModelOptions.FromConfiguration(Config(new()
        {
            [ConfigKeys.Ai.RoundModel] = "cheap-tier",
            [ConfigKeys.Ai.JudgeModel] = " smart-tier ",
            [ConfigKeys.Ai.TtsModel] = "   ",
            [ConfigKeys.Ai.JudgeServiceTier] = "standard",
        }));

        models.Round.Should().Be("cheap-tier");
        models.Judge.Should().Be("smart-tier", "ids are trimmed");
        models.Tts.Should().Be(GeminiModelOptions.Defaults.Tts, "a blank key keeps its default");
        models.Live.Should().Be(GeminiModelOptions.Defaults.Live);
        models.JudgeServiceTier.Should().Be("standard");
    }
}
