using PoFightJudge.Api.Features.Analysis;
using PoFightJudge.Api.Features.Fight;
using PoFightJudge.Api.Features.Storage;
using PoFightJudge.Api.Features.Voice;

namespace PoFightJudge.Unit.Analysis;

/// <summary>
/// Getting a recording from storage to the model without holding it in memory several times over.
/// </summary>
/// <remarks>
/// A fifteen-minute fight is about 28 MB as 16 kHz mono PCM. The pipeline used to copy the blob into a MemoryStream
/// that doubles as it grows, call ToArray on it, and hand the copy on — two full copies live at once, held for the
/// length of the whole analysis because the local stayed in scope past the upload. Two of those may run at a time
/// (MaxParallelAnalyses) on an F1 plan with 1 GB of RAM.
///
/// A WAV on disk is already exactly what the model reads, so it does not need to be in memory at all. Opus does:
/// a decoder needs the whole file. What that path can stop doing is materialising the decoded audio three times —
/// once in the decoder's growing buffer, once in its ToArray, and once more in the WAV built around it.
/// </remarks>
public class RecordingUploadTests
{
    private const int Rate = DebateOrchestrator.PlayerSampleRate;

    private static byte[] Tone(int samples)
    {
        var pcm = new byte[samples * 2];
        for (var i = 0; i < samples; i++)
        {
            var value = (short)(Math.Sin(i * 0.05) * 8000);
            pcm[i * 2] = (byte)(value & 0xFF);
            pcm[(i * 2) + 1] = (byte)((value >> 8) & 0xFF);
        }

        return pcm;
    }

    [Fact]
    public async Task A_wav_already_on_disk_is_sent_as_it_lies_rather_than_copied_through_memory()
    {
        var wav = WavWriter.Build(Rate, Tone(Rate));
        var store = new StubBlobs(wav);

        await using var recording = await RecordingSource.OpenAsync(store, "m/players.wav", Rate, CancellationToken.None);

        recording.Length.Should().Be(wav.Length);
        recording.Content.Should().BeSameAs(store.Opened, "the blob's own stream is what gets uploaded");
    }

    [Fact]
    public async Task An_opus_recording_is_decoded_to_the_wav_the_model_will_read()
    {
        var pcm = Tone(Rate);
        var store = new StubBlobs(OpusAudio.Encode(pcm, Rate));

        await using var recording = await RecordingSource.OpenAsync(store, "m/players.opus", Rate, CancellationToken.None);

        var read = new MemoryStream();
        await recording.Content.CopyToAsync(read);
        var bytes = read.ToArray();

        bytes.Length.Should().Be((int)recording.Length, "the length handed to the upload is the length that follows it");
        Transcripts.LooksLikeWav(bytes).Should().BeTrue("the Files API refuses Ogg at generateContent — measured 2026-09-07");
        bytes.Length.Should().BeGreaterThan(pcm.Length, "a WAV is its samples plus a header");
    }

    /// <summary>
    /// The decoded audio used to be built three times over. This pins the one thing a caller can actually observe
    /// about that: what comes out is a single WAV whose header agrees with the bytes behind it.
    /// </summary>
    [Fact]
    public async Task The_decoded_wav_declares_the_length_it_actually_carries()
    {
        var store = new StubBlobs(OpusAudio.Encode(Tone(Rate * 2), Rate));

        await using var recording = await RecordingSource.OpenAsync(store, "m/players.opus", Rate, CancellationToken.None);

        var read = new MemoryStream();
        await recording.Content.CopyToAsync(read);
        var bytes = read.ToArray();

        var declared = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(40));
        declared.Should().Be(bytes.Length - 44);
        System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)).Should().Be(bytes.Length - 8);
    }

    [Fact]
    public async Task A_recording_that_is_not_in_storage_says_so_rather_than_uploading_nothing()
    {
        var open = async () => await RecordingSource.OpenAsync(new StubBlobs(null), "m/players.wav", Rate, CancellationToken.None);

        await open.Should().ThrowAsync<InvalidOperationException>().WithMessage("*missing*");
    }

    private sealed class StubBlobs(byte[]? content) : IAudioBlobStore
    {
        public Stream? Opened { get; private set; }

        public Task<string> UploadAsync(string blobName, Stream content, string contentType, CancellationToken ct = default) =>
            Task.FromResult(blobName);

        public Task<Stream?> OpenReadAsync(string blobName, CancellationToken ct = default)
        {
            Opened = content is null ? null : new MemoryStream(content, writable: false);
            return Task.FromResult(Opened);
        }

        public Task DeleteMatchAudioAsync(PoFightJudge.Shared.Identifiers.MatchId matchId, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}

/// <summary>
/// The one knob in <see cref="AnalysisOptions"/> that can stop the pipeline dead.
/// </summary>
public class ClientTranscriptWaitTests
{
    /// <summary>
    /// The wait runs on TimeProvider, so a non-zero value against a clock nobody advances never returns — which is
    /// exactly what happened the moment AcceptClientTranscript was turned on with the old three-second default:
    /// every pipeline test whose fight had no live captions hung instead of failing.
    ///
    /// Zero means "take it if it is already there", which is the case that actually happens: the browser posts as
    /// the fight ends, and the recording's upload runs ahead of this check.
    /// </summary>
    [Fact]
    public void The_browser_transcript_is_taken_if_it_is_there_and_never_waited_for_by_default()
    {
        var options = new AnalysisOptions();

        options.AcceptClientTranscript.Should().BeTrue("the browser half exists now, and it is free");
        options.ClientTranscriptWaitSeconds.Should().Be(0, "a wait on a clock nobody advances is a hang, not a delay");
    }
}
