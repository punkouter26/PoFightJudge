using PoMarriedFight.Api.Features.Voice;

namespace PoMarriedFight.Unit.Voice;

public class TtsAudioFormatsTests
{
    [Theory]
    [InlineData(null, TtsAudioFormats.Mp3)]
    [InlineData("", TtsAudioFormats.Mp3)]
    [InlineData("mp3", TtsAudioFormats.Mp3)]
    [InlineData(" PCM ", TtsAudioFormats.Pcm)]
    [InlineData("wav", TtsAudioFormats.Mp3)]
    public void Normalize_defaults_typos_to_mp3_and_recognizes_pcm_case_insensitively(string? configured, string expected) =>
        TtsAudioFormats.Normalize(configured).Should().Be(expected);

    [Fact]
    public void Extensions_and_mime_types_follow_the_format_and_none_is_empty_pcm()
    {
        TtsAudioFormats.Extension(TtsAudioFormats.Mp3).Should().Be("mp3");
        TtsAudioFormats.Extension(TtsAudioFormats.Pcm).Should().Be("pcm");
        TtsAudioFormats.MimeType("MP3").Should().Be("audio/mpeg");
        TtsAudioFormats.MimeType(TtsAudioFormats.Pcm).Should().Be("audio/pcm");

        TtsAudio.None.IsEmpty.Should().BeTrue();
        TtsAudio.None.Format.Should().Be(TtsAudioFormats.Pcm);
        TtsAudio.Pcm("AAAA").Should().Be(new TtsAudio("AAAA", TtsAudioFormats.Pcm));
        TtsAudio.Mp3("AAAA").Format.Should().Be(TtsAudioFormats.Mp3);
    }
}
