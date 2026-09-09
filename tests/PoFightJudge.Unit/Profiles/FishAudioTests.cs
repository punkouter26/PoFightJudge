using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PoFightJudge.Api.Features.Profiles;
using PoFightJudge.Api.Features.Voice;

namespace PoFightJudge.Unit.Profiles;

/// <summary>
/// The cloned-voice provider and the chain in front of it. The failure this guards against is quiet rather than
/// loud: when Fish cannot speak, the line still plays — in somebody else's voice — and a persona that exists to
/// sound like one particular person sounds like a stock voice instead, with nothing on screen to say so.
/// </summary>
public class FishAudioTests
{
    private static TtsSettings Voiced(string? referenceId) => new(1.0, 1.0, "Charon", referenceId);

    private static FishAudioService Fish(bool enabled, string? shared = null) =>
        new(Substitute.For<IHttpClientFactory>(),
            new FishAudioOptions(enabled, shared),
            new TtsRoutingOptions("mp3", CacheEnabled: false),
            new PoFightJudge.Api.Features.Diagnostics.AiLatencyTracker(),
            NullLogger<FishAudioService>.Instance);

    [Fact]
    public void A_persona_with_its_own_voice_is_spoken_by_fish()
    {
        Fish(enabled: true).CanSpeak(Voiced("trump")).Should().BeTrue();
    }

    [Fact]
    public void A_persona_without_one_falls_to_the_ordinary_voice()
    {
        Fish(enabled: true).CanSpeak(Voiced(null)).Should().BeFalse();
        Fish(enabled: true).CanSpeak(Voiced("  ")).Should().BeFalse("whitespace is not a voice id");
    }

    [Fact]
    public void A_shared_default_voice_covers_a_persona_that_named_none()
    {
        Fish(enabled: true, shared: "house-voice").CanSpeak(Voiced(null)).Should().BeTrue();
        Fish(enabled: true, shared: "house-voice").ReferenceIdFor(Voiced("trump"))
            .Should().Be("trump", "a persona's own voice always beats the shared one");
    }

    [Fact]
    public void Without_a_key_fish_never_speaks_however_many_ids_are_set()
    {
        Fish(enabled: false, shared: "house-voice").CanSpeak(Voiced("trump")).Should().BeFalse();
    }

    [Fact]
    public void Mp3_is_asked_for_at_a_rate_fish_accepts()
    {
        // Fish rejects any other rate for mp3 with a 400, and a 400 here is invisible: the chain falls through and
        // the line simply plays in the wrong voice.
        FishAudioService.SampleRateFor("mp3").Should().Be(44_100);
        FishAudioService.SampleRateFor("pcm").Should().Be(24_000);
    }

    [Fact]
    public void The_free_model_is_the_one_asked_for()
    {
        FishAudioService.Model.Should().Be("s2.1-pro-free", "s2-pro needs paid credit and 402s without it");
    }

    [Fact]
    public void A_cloned_voice_and_a_stock_one_never_share_a_cache_entry()
    {
        var cloned = CachingTtsService.CacheKey("that is a disgrace", Voiced("trump"), "fish", "mp3");
        var stock = CachingTtsService.CacheKey("that is a disgrace", Voiced(null), "gemini", "mp3");

        cloned.Should().NotBe(stock);
    }

    [Fact]
    public void Two_different_cloned_voices_never_share_a_cache_entry()
    {
        var trump = CachingTtsService.CacheKey("say it again", Voiced("trump"), "fish", "mp3");
        var clinton = CachingTtsService.CacheKey("say it again", Voiced("clinton"), "fish", "mp3");

        trump.Should().NotBe(clinton, "the reference id is what makes the rendering different");
    }
}
