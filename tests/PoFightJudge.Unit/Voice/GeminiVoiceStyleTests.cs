using PoFightJudge.Api.Features.Profiles;
using PoFightJudge.Api.Features.Voice;
using PoFightJudge.Shared.Models;
using PoFightJudge.Shared.Validators;

namespace PoFightJudge.Unit.Voice;

/// <summary>Gemini has no accent knob, so a persona's accent rides in the spoken-style prefix with pitch and speed.</summary>
public class GeminiVoiceStyleTests
{
    [Fact]
    public void An_accent_is_asked_for_ahead_of_pitch_and_speed()
    {
        GeminiTtsService.ApplyVoiceStyle("Me? What about YOU?", new TtsSettings(1.4, 1.3, "Kore", Accent: "Filipino"))
            .Should().Be("Say in a Filipino accent, in a high-pitched voice, quickly: Me? What about YOU?");
    }

    [Fact]
    public void No_accent_and_neutral_prosody_leave_the_line_alone()
    {
        GeminiTtsService.ApplyVoiceStyle("Fine.", new TtsSettings(1.0, 1.0, "Kore", Accent: "  ")).Should().Be("Fine.");
    }

    [Fact]
    public void A_blank_accent_keeps_the_cache_key_it_had_and_a_set_one_splits_it()
    {
        CachingTtsService.CacheKey("Fine.", new TtsSettings(1.0, 1.0, "Kore", Accent: " "), "gemini", "mp3")
            .Should().Be("gemini|mp3|Kore||1.00|1.00|Fine.");
        CachingTtsService.CacheKey("Fine.", new TtsSettings(1.0, 1.0, "Kore", Accent: "Filipino "), "gemini", "mp3")
            .Should().Be("gemini|mp3|Kore||Filipino|1.00|1.00|Fine.");
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
