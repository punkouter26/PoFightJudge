using PoFightJudge.Api.Features.Voice;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Api.Features.Analysis;

/// <summary>
/// A finished fight, as the two transcribers each need it: one reads it from a Files API URI it has already been
/// uploaded to, the other has to send the bytes. <paramref name="OpenAsync"/> is a factory rather than a stream so
/// that the service which does not need the audio never opens it.
/// </summary>
public sealed record RecordingRef(string FileUri, string MimeType, Func<CancellationToken, Task<Recording>> OpenAsync);

/// <summary>
/// Diarized transcription of a whole recording, used when the host's live captions are missing or too thin. One
/// implementation is chosen at startup, best first — the same shape <c>AddPoVoice</c> uses for turn transcription,
/// and for the same reason: this is a fallback already, and a fallback with its own fallback is a way to pay twice.
/// </summary>
public interface IRecordingTranscriber
{
    /// <summary>Short stable name for the log line that says which source a fight was read from.</summary>
    string Name { get; }

    Task<TranscriptDto> TranscribeAsync(RecordingRef recording, CancellationToken ct);
}

/// <summary>Which service reads a finished fight.</summary>
public static class RecordingTranscribers
{
    /// <summary>
    /// Azure whenever it is configured. It diarizes, it returns word offsets, and — unlike the transcribe model,
    /// which <see cref="GeminiTranscriptionService"/>'s remarks record answering 200 with an empty part while
    /// billing the audio — it has been seen to work, because it is what transcribes every WATCH turn.
    /// </summary>
    public static bool PrefersAzure(AzureSpeechOptions azure)
    {
        ArgumentNullException.ThrowIfNull(azure);
        return azure.Enabled;
    }
}

/// <summary>The transcribe model, behind the seam. It reads the file the pipeline has already uploaded.</summary>
public sealed class GeminiRecordingTranscriber(IGeminiTranscribeClient inner) : IRecordingTranscriber
{
    public string Name => "gemini-transcribe";

    public Task<TranscriptDto> TranscribeAsync(RecordingRef recording, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(recording);
        return inner.TranscribeAsync(recording.FileUri, recording.MimeType, ct);
    }
}
