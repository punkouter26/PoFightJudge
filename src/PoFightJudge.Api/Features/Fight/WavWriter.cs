using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace PoFightJudge.Api.Features.Fight;

/// <summary>Accumulates 16-bit mono PCM and renders a RIFF/WAVE file. Safe for one writer and any number of readers.</summary>
public sealed class WavWriter(int sampleRate) : IDisposable
{
    private readonly MemoryStream _pcm = new();
    private readonly Lock _lock = new();

    public int SampleRate { get; } = sampleRate;

    public long Bytes
    {
        get
        {
            lock (_lock)
            {
                return _pcm.Length;
            }
        }
    }

    public TimeSpan Duration => TimeSpan.FromSeconds(Bytes / 2.0 / SampleRate);

    public void Append(ReadOnlySpan<byte> pcm16)
    {
        lock (_lock)
        {
            _pcm.Write(pcm16);
        }
    }

    /// <summary>
    /// Appends silence so a sparse track lines up with wall-clock time. The host only speaks in bursts, and without
    /// the gaps its track would compress into one continuous monologue that matches nothing else in the recording.
    /// </summary>
    public void PadTo(TimeSpan position)
    {
        var targetBytes = (long)(position.TotalSeconds * SampleRate) * 2;
        lock (_lock)
        {
            if (targetBytes > _pcm.Length)
            {
                _pcm.SetLength(targetBytes);
                _pcm.Position = targetBytes;
            }
        }
    }

    public byte[] ToWav()
    {
        byte[] data;
        lock (_lock)
        {
            data = _pcm.ToArray();
        }

        return Build(SampleRate, data);
    }

    /// <summary>A readable WAV stream over the recorded buffer without copying the PCM. Call once recording has stopped.</summary>
    /// <summary>The raw samples, for a caller that wants to encode them rather than read a WAV.</summary>
    public ReadOnlyMemory<byte> Pcm
    {
        get
        {
            lock (_lock)
            {
                return new ReadOnlyMemory<byte>(_pcm.GetBuffer(), 0, (int)_pcm.Length);
            }
        }
    }

    public Stream OpenWavStream()
    {
        lock (_lock)
        {
            var length = (int)_pcm.Length;
            var header = Build(SampleRate, ReadOnlySpan<byte>.Empty);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), 36 + length);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(40), length);
            return new ConcatenatedStream(new MemoryStream(header), new MemoryStream(_pcm.GetBuffer(), 0, length, writable: false));
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _pcm.Dispose();
        }
    }

    public static byte[] Build(int sampleRate, ReadOnlySpan<byte> pcm16)
    {
        var header = new byte[44];
        "RIFF"u8.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), 36 + pcm16.Length);
        "WAVE"u8.CopyTo(header.AsSpan(8));
        "fmt "u8.CopyTo(header.AsSpan(12));
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(20), 1);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(22), 1);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(24), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(28), sampleRate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(32), 2);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(34), 16);
        "data"u8.CopyTo(header.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(40), pcm16.Length);

        var wav = new byte[44 + pcm16.Length];
        header.CopyTo(wav, 0);
        pcm16.CopyTo(wav.AsSpan(44));
        return wav;
    }

    /// <summary>Root-mean-square of a 16-bit little-endian PCM frame, 0 to 1. This is what the silence gate reads.</summary>
    public static double Rms(ReadOnlySpan<byte> pcm16)
    {
        var samples = MemoryMarshal.Cast<byte, short>(pcm16);
        if (samples.Length == 0)
        {
            return 0;
        }

        double sum = 0;
        foreach (var sample in samples)
        {
            var scaled = sample / 32768.0;
            sum += scaled * scaled;
        }

        return Math.Sqrt(sum / samples.Length);
    }

    /// <summary>Read-only stream that reads its parts in order, so the header and the PCM upload as one file.</summary>
    private sealed class ConcatenatedStream(params Stream[] parts) : Stream
    {
        private int _index;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => parts.Sum(p => p.Length);

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            while (_index < parts.Length)
            {
                var read = parts[_index].Read(buffer);
                if (read > 0)
                {
                    return read;
                }

                _index++;
            }

            return 0;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
