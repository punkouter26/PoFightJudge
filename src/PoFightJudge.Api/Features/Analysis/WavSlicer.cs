using System.Buffers.Binary;
using PoFightJudge.Api.Features.Storage;

namespace PoFightJudge.Api.Features.Analysis;

/// <summary>
/// Cuts a playable clip out of a recording. A highlight is only worth showing if it can be heard, and the browser
/// cannot seek inside a private blob — so the slice is made here and served as a small file of its own.
/// </summary>
public static class WavSlicer
{
    /// <summary>Where the data of a canonical 16-bit mono PCM WAV starts, and how its header is laid out.</summary>
    public const int HeaderBytes = 44;

    /// <summary>
    /// The bytes between two offsets of a stored recording, as a WAV in its own right. Recordings are kept in
    /// Opus, which cannot be cut at an arbitrary offset, so an Opus recording is decoded first — the clip that
    /// comes out is a WAV either way, because that is what a browser can be handed and play.
    /// </summary>
    public static byte[]? SliceRecording(byte[] recording, string? blobName, int sampleRate, double startSeconds, double endSeconds)
    {
        ArgumentNullException.ThrowIfNull(recording);
        return Slice(
            OpusAudio.IsOpus(blobName) ? OpusAudio.DecodeToWav(recording, sampleRate) : recording,
            startSeconds,
            endSeconds);
    }

    /// <summary>
    /// The bytes between two offsets, as a WAV in its own right. A range that falls outside the recording is
    /// clamped rather than refused: a highlight near the end is still worth hearing.
    /// </summary>
    public static byte[]? Slice(byte[] wav, double startSeconds, double endSeconds)
    {
        ArgumentNullException.ThrowIfNull(wav);
        if (wav.Length <= HeaderBytes || endSeconds <= startSeconds)
        {
            return null;
        }

        var sampleRate = BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(24));
        var channels = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(22));
        var bits = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(34));
        if (sampleRate <= 0 || channels <= 0 || bits != 16)
        {
            return null;
        }

        var bytesPerSecond = sampleRate * channels * (bits / 8);
        var available = wav.Length - HeaderBytes;

        // Aligned to a whole sample: starting mid-sample turns the first moment of a clip into a click.
        var blockAlign = channels * (bits / 8);
        var from = Align(Math.Clamp((long)(startSeconds * bytesPerSecond), 0, available), blockAlign);
        var to = Align(Math.Clamp((long)(endSeconds * bytesPerSecond), 0, available), blockAlign);
        if (to <= from)
        {
            return null;
        }

        var length = (int)(to - from);
        var clip = new byte[HeaderBytes + length];
        wav.AsSpan(0, HeaderBytes).CopyTo(clip);
        wav.AsSpan(HeaderBytes + (int)from, length).CopyTo(clip.AsSpan(HeaderBytes));
        BinaryPrimitives.WriteInt32LittleEndian(clip.AsSpan(4), 36 + length);
        BinaryPrimitives.WriteInt32LittleEndian(clip.AsSpan(40), length);
        return clip;
    }

    private static long Align(long offset, int blockAlign) => blockAlign <= 0 ? offset : offset - (offset % blockAlign);
}
