using System.Buffers.Binary;
using PoFightJudge.Api.Features.Fight;
using PoFightJudge.Api.Features.Storage;

namespace PoFightJudge.Unit.Records;

/// <summary>
/// Recordings are stored in Opus and read back as PCM. What matters is that the length survives — the analysis
/// reads its timings off it and every highlight is cut by the second — and that the file is actually smaller,
/// which is the only reason to do this at all.
/// </summary>
public class OpusAudioTests
{
    private const int Rate = 16_000;

    /// <summary>Something speech-like rather than silence: a quiet stretch would compress no matter what.</summary>
    private static byte[] Speech(double seconds, int sampleRate = Rate)
    {
        var samples = (int)(seconds * sampleRate);
        var pcm = new byte[samples * 2];
        for (var i = 0; i < samples; i++)
        {
            var t = (double)i / sampleRate;

            // Two tones and an envelope, roughly where a voice sits.
            var value = (Math.Sin(2 * Math.PI * 220 * t) * 0.4) + (Math.Sin(2 * Math.PI * 440 * t) * 0.2);
            value *= 0.5 + (0.5 * Math.Sin(2 * Math.PI * 3 * t));
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2), (short)(value * short.MaxValue * 0.8));
        }

        return pcm;
    }

    private static TimeSpan Length(byte[] pcm, int sampleRate = Rate) => TimeSpan.FromSeconds(pcm.Length / 2.0 / sampleRate);

    [Theory]
    [InlineData(16_000)]
    [InlineData(24_000)]
    public void A_recording_comes_back_the_length_it_went_in(int sampleRate)
    {
        var pcm = Speech(3.0, sampleRate);

        var back = OpusAudio.Decode(OpusAudio.Encode(pcm, sampleRate), sampleRate);

        Length(back, sampleRate).Should().BeCloseTo(Length(pcm, sampleRate), TimeSpan.FromMilliseconds(120),
            "every highlight is cut by the second, so the recording cannot drift");
    }

    [Fact]
    public void A_recording_that_does_not_end_on_a_frame_is_not_cut_short()
    {
        // 2.507 seconds: a fight ends when somebody stops talking, not on a twenty-millisecond boundary.
        var pcm = Speech(2.507);

        var back = OpusAudio.Decode(OpusAudio.Encode(pcm, Rate), Rate);

        Length(back).Should().BeGreaterThanOrEqualTo(Length(pcm), "the tail is padded rather than dropped");
    }

    [Fact]
    public void The_whole_point_is_that_it_is_smaller()
    {
        var pcm = Speech(10.0);
        var wav = WavWriter.Build(Rate, pcm);

        var opus = OpusAudio.Encode(pcm, Rate);

        opus.Length.Should().BeLessThan(wav.Length / 5, "speech in Opus is a fraction of the same speech as PCM");
        opus.Length.Should().BeGreaterThan(1_000, "and it is still a real recording, not an empty container");
    }

    [Fact]
    public void What_comes_back_still_sounds_like_what_went_in()
    {
        var pcm = Speech(2.0);

        var back = OpusAudio.Decode(OpusAudio.Encode(pcm, Rate), Rate);

        // Opus is lossy, so this is not a comparison of samples: it is whether the sound is still there at
        // roughly the level it was, which is what a transcriber and a listener both need.
        Rms(back).Should().BeApproximately(Rms(pcm), Rms(pcm) * 0.4);

        static double Rms(byte[] pcm16) => WavWriter.Rms(pcm16);
    }

    [Fact]
    public void A_recording_read_back_as_a_wav_is_a_wav_the_slicer_understands()
    {
        var pcm = Speech(2.0);

        var wav = OpusAudio.DecodeToWav(OpusAudio.Encode(pcm, Rate), Rate);

        BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(24)).Should().Be(Rate);
        BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(34)).Should().Be(16, "sixteen-bit, which is what the slicer requires");
        wav.Length.Should().BeGreaterThan(44);
    }

    [Fact]
    public void A_rate_Opus_does_not_record_at_is_refused_rather_than_quietly_resampled()
    {
        OpusAudio.IsSupportedRate(16_000).Should().BeTrue();
        OpusAudio.IsSupportedRate(44_100).Should().BeFalse();

        var act = () => OpusAudio.Encode(Speech(0.1), 44_100);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("abc/players.opus", true)]
    [InlineData("abc/players.wav", false)]
    [InlineData(null, false)]
    public void A_stored_recording_says_which_it_is_in_its_name(string? blobName, bool opus) =>
        OpusAudio.IsOpus(blobName).Should().Be(opus);
}
