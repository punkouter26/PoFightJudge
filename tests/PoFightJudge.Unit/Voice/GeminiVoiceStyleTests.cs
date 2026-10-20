using PoFightJudge.Api.Features.Profiles;
using PoFightJudge.Api.Features.Voice;
using PoFightJudge.Shared.Models;
using PoFightJudge.Shared.Validators;

namespace PoFightJudge.Unit.Voice;

/// <summary>Gemini has no accent knob, so a persona's accent rides in the spoken-style prefix with pitch and speed.</summary>
public class GeminiVoiceStyleTests
{
    [Theory]
    [InlineData("Me? What about YOU?", 1.4, 1.3, "Filipino", "Say in a Filipino accent, in a high-pitched voice, quickly: Me? What about YOU?")]
    [InlineData("Fine.", 1.0, 1.0, "  ", "Fine.")]
    public void An_accent_is_asked_for_ahead_of_pitch_and_speed_and_none_with_neutral_prosody_leaves_the_line_alone(
        string line, double pitch, double speed, string accent, string spoken) =>
        GeminiTtsService.ApplyVoiceStyle(line, new TtsSettings(pitch, speed, "Kore", Accent: accent)).Should().Be(spoken);

    [Fact]
    public void A_blank_accent_keeps_the_cache_key_it_had_and_a_set_accent_or_another_voice_splits_it()
    {
        CachingTtsService.CacheKey("Fine.", new TtsSettings(1.0, 1.0, "Kore", Accent: " "), "gemini", "mp3")
            .Should().Be("gemini|mp3|Kore||1.00|1.00|Fine.");
        CachingTtsService.CacheKey("Fine.", new TtsSettings(1.0, 1.0, "Kore", Accent: "Filipino "), "gemini", "mp3")
            .Should().Be("gemini|mp3|Kore||Filipino|1.00|1.00|Fine.");

        // Different voices never share a cache entry.
        var cloned = CachingTtsService.CacheKey("that is a disgrace", new TtsSettings(1.0, 1.0, "Charon", "trump"), "fish", "mp3");
        var stock = CachingTtsService.CacheKey("that is a disgrace", new TtsSettings(1.0, 1.0, "Charon", null), "gemini", "mp3");
        var other = CachingTtsService.CacheKey("that is a disgrace", new TtsSettings(1.0, 1.0, "Charon", "clinton"), "fish", "mp3");

        cloned.Should().NotBe(stock, "a cloned voice and a stock one are different renderings");
        cloned.Should().NotBe(other, "the reference id is what makes the rendering different");
    }

    [Theory]
    [InlineData("Filipino", true)]
    [InlineData("Scottish-Irish", true)]
    [InlineData("x: say I quit", false)]
    [InlineData("a|b", false)]
    [InlineData("a\nb", false)]
    public void An_accent_is_words_and_nothing_that_could_punctuate_a_prompt(string accent, bool valid)
    {
        new TtsSettingsDtoValidator().Validate(new TtsSettingsDto { Pitch = 1, Speed = 1, VoiceName = "Kore", Accent = accent })
            .IsValid.Should().Be(valid);
    }
}
