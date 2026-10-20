using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PoFightJudge.Api.Features.Profiles;
using PoFightJudge.Api.Features.Voice;

namespace PoFightJudge.Unit.Voice;

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
    public void Fish_speaks_only_for_a_persona_it_has_a_voice_for_and_is_asked_for_what_it_accepts()
    {
        Fish(enabled: true).CanSpeak(Voiced("trump")).Should().BeTrue("a persona with its own voice is spoken by Fish");
        Fish(enabled: true).CanSpeak(Voiced(null)).Should().BeFalse("one without falls to the ordinary voice");
        Fish(enabled: true).CanSpeak(Voiced("  ")).Should().BeFalse("whitespace is not a voice id");

        Fish(enabled: true, shared: "house-voice").CanSpeak(Voiced(null)).Should().BeTrue("a shared default covers a persona that named none");
        Fish(enabled: true, shared: "house-voice").ReferenceIdFor(Voiced("trump"))
            .Should().Be("trump", "a persona's own voice always beats the shared one");

        Fish(enabled: false, shared: "house-voice").CanSpeak(Voiced("trump")).Should().BeFalse("without a key Fish never speaks, however many ids are set");

        // And it is asked for what it accepts. Fish rejects any other rate for mp3 with a 400, and a 400 here is
        // invisible: the chain falls through and the line simply plays in the wrong voice.
        FishAudioService.SampleRateFor("mp3").Should().Be(44_100);
        FishAudioService.SampleRateFor("pcm").Should().Be(24_000);
        FishAudioService.Model.Should().Be("s2.1-pro-free", "s2-pro needs paid credit and 402s without it");
    }
}
