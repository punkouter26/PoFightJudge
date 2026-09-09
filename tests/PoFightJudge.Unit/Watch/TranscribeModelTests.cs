using Microsoft.Extensions.Configuration;
using PoFightJudge.Api.Features.Ai;
using PoFightJudge.Shared.Configuration;

namespace PoFightJudge.Unit.Watch;

/// <summary>
/// Two different Gemini surfaces transcribe in this solution, and they do not accept the same model. The diarizing
/// interactions call wants the transcribe tier; a live turn goes through generateContent, where that tier answers
/// 200 with an empty part and hands the player back a turn that heard nothing. One key served both, which is how
/// that reached production silently.
/// </summary>
public class TranscribeModelTests
{
    private static GeminiModelOptions FromSettings(params (string Key, string Value)[] settings) =>
        GeminiModelOptions.FromConfiguration(new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build());

    [Fact]
    public void A_live_turn_does_not_use_the_diarization_model()
    {
        GeminiModelOptions.Defaults.TurnTranscribe.Should().NotBe(
            GeminiModelOptions.Defaults.Transcribe,
            "the transcribe tier returns an empty part on generateContent, so sharing one id silently empties every spoken turn");
    }

    [Fact]
    public void A_live_turn_defaults_to_the_tier_that_reads_a_clip_back()
    {
        GeminiModelOptions.Defaults.TurnTranscribe.Should().Be("gemini-3.1-flash-lite");
    }

    [Fact]
    public void Diarization_keeps_the_transcribe_tier()
    {
        GeminiModelOptions.Defaults.Transcribe.Should().Be("gemini-3.5-transcribe", "the interactions API is where that tier is the right answer");
    }

    [Fact]
    public void Each_surface_is_repointed_on_its_own()
    {
        var models = FromSettings(
            (ConfigKeys.Ai.TranscribeModel, "diarizer-9"),
            (ConfigKeys.Ai.TurnTranscribeModel, "turns-9"));

        models.Transcribe.Should().Be("diarizer-9");
        models.TurnTranscribe.Should().Be("turns-9", "repointing one surface must never move the other");
    }

    [Fact]
    public void An_unset_turn_model_falls_back_without_borrowing_the_diarizer()
    {
        var models = FromSettings((ConfigKeys.Ai.TranscribeModel, "diarizer-9"));

        models.TurnTranscribe.Should().Be(GeminiModelOptions.Defaults.TurnTranscribe);
    }
}
