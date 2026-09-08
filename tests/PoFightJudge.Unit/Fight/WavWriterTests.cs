using System.Buffers.Binary;
using PoFightJudge.Api.Features.Fight;

namespace PoFightJudge.Unit.Fight;

public class WavWriterTests
{
    private const int Rate = 16_000;

    private static byte[] Tone(int samples)
    {
        var pcm = new byte[samples * 2];
        for (var i = 0; i < samples; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2), (short)(i % 2 == 0 ? 8000 : -8000));
        }

        return pcm;
    }

    [Fact]
    public void A_recorded_track_comes_out_as_a_playable_wav()
    {
        using var writer = new WavWriter(Rate);
        writer.Append(Tone(Rate));

        var wav = writer.ToWav();

        wav.Should().HaveCount(44 + (Rate * 2));
        System.Text.Encoding.ASCII.GetString(wav, 0, 4).Should().Be("RIFF");
        System.Text.Encoding.ASCII.GetString(wav, 8, 4).Should().Be("WAVE");
        BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(4)).Should().Be(36 + (Rate * 2), "the RIFF size counts everything after it");
        BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(22)).Should().Be(1, "one channel");
        BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(24)).Should().Be(Rate);
        BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(34)).Should().Be(16, "16-bit samples");
        BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(40)).Should().Be(Rate * 2, "the data chunk is the PCM length");
        writer.Duration.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void An_empty_track_is_still_a_valid_file()
    {
        using var empty = new WavWriter(Rate);
        var wav = empty.ToWav();

        wav.Should().HaveCount(44);
        BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(40)).Should().Be(0);
    }

    [Fact]
    public void Padding_lines_a_sparse_track_up_with_the_clock_and_never_rewinds_it()
    {
        using var writer = new WavWriter(Rate);
        writer.Append(Tone(Rate / 2));

        writer.PadTo(TimeSpan.FromSeconds(2));
        writer.Duration.Should().Be(TimeSpan.FromSeconds(2), "the host is quiet between turns, and the silence has to be recorded");

        writer.PadTo(TimeSpan.FromSeconds(1));
        writer.Duration.Should().Be(TimeSpan.FromSeconds(2), "padding backwards would drop audio that was already recorded");

        writer.Append(Tone(Rate));
        writer.Duration.Should().Be(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void The_streamed_file_matches_the_one_built_in_memory()
    {
        using var writer = new WavWriter(Rate);
        writer.Append(Tone(1_000));

        using var stream = writer.OpenWavStream();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);

        copy.ToArray().Should().Equal(writer.ToWav(), "the upload path and the in-memory path must produce the same file");
    }

    [Fact]
    public void Loudness_is_measured_the_way_the_silence_gate_reads_it()
    {
        WavWriter.Rms(Tone(100)).Should().BeApproximately(8000 / 32768.0, 0.001);
        WavWriter.Rms(new byte[200]).Should().Be(0, "digital silence is silent");
        WavWriter.Rms([]).Should().Be(0, "an empty frame is not a divide by zero");
    }
}
