using Microsoft.Extensions.Configuration;
using PoFightJudge.Api.Features.Ai;
using PoFightJudge.Shared.Configuration;

namespace PoFightJudge.Unit.Watch;

/// <summary>
/// Two different Gemini surfaces transcribe in this solution, and they do not accept the same model.
/// Live turn uses flash-lite for generateContent, whereas diarizing interactions uses transcribe.
/// </summary>
public class TranscribeModelTests
{
    private static GeminiModelOptions FromSettings(params (string Key, string Value)[] settings) =>
        GeminiModelOptions.FromConfiguration(new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build());

    [Fact]
    public void Live_turn_and_diarization_models_have_distinct_defaults()
    {
        GeminiModelOptions.Defaults.TurnTranscribe.Should().NotBe(
            GeminiModelOptions.Defaults.Transcribe,
            "the transcribe tier returns an empty part on generateContent, so sharing one id silently empties every spoken turn");
        GeminiModelOptions.Defaults.TurnTranscribe.Should().Be("gemini-3.1-flash-lite");
        GeminiModelOptions.Defaults.Transcribe.Should().Be("gemini-3.5-transcribe", "the interactions API is where that tier is the right answer");
    }

    [Fact]
    public void Surfaces_are_configured_independently_with_proper_fallback()
    {
        var models = FromSettings(
            (ConfigKeys.Ai.TranscribeModel, "diarizer-9"),
            (ConfigKeys.Ai.TurnTranscribeModel, "turns-9"));

        models.Transcribe.Should().Be("diarizer-9");
        models.TurnTranscribe.Should().Be("turns-9", "repointing one surface must never move the other");

        var fallback = FromSettings((ConfigKeys.Ai.TranscribeModel, "diarizer-9"));
        fallback.TurnTranscribe.Should().Be(GeminiModelOptions.Defaults.TurnTranscribe);
    }
}
