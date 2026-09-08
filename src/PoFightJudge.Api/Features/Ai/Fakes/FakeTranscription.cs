using PoFightJudge.Api.Features.Voice;

namespace PoFightJudge.Api.Features.Ai.Fakes;

/// <summary>
/// Offline stand-in for the transcription providers, registered rather than disabled so the whole SELF turn —
/// permission, meter, record, submit, opponent's reply — is exercisable with no network and no key.
/// </summary>
/// <remarks>
/// The canned lines say so out loud. Silently substituting plausible text would make a dead transcription provider
/// indistinguishable from a bad microphone, which is exactly the confusion the fake-AI banner exists to prevent.
/// A clip that is not a WAV, or is silent, still comes back empty, so the "we didn't catch that" path is reachable.
/// </remarks>
public sealed partial class FakeTranscription(ILogger<FakeTranscription> logger) : ITranscriptionService
{
    private static readonly string[] CannedLines =
    [
        "(fake transcript) Oh that is rich, coming from you of all people.",
        "(fake transcript) I did not raise my voice — you raised yours first.",
        "(fake transcript) Fine. Let's talk about what you did last weekend then.",
        "(fake transcript) You always do this when you know you are losing.",
    ];

    /// <summary>Below this a clip is treated as a slip of the button rather than a turn.</summary>
    public const int MinAudibleBytes = 2048;

    public string Name => "fake";

    public bool IsEnabled => true;

    public Task<string> TranscribeAsync(byte[] wav, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(wav);
        LogTranscribed(logger, wav.Length);

        if (wav.Length < MinAudibleBytes || !Transcripts.LooksLikeWav(wav))
        {
            return Task.FromResult(string.Empty);
        }

        // Deterministic in the clip's length, so a test that submits the same clip twice gets the same line.
        return Task.FromResult(CannedLines[wav.Length % CannedLines.Length]);
    }

    [LoggerMessage(EventId = 6104, Level = LogLevel.Information, Message = "Fake transcription answered a {Bytes}-byte clip with a canned line")]
    private static partial void LogTranscribed(ILogger logger, int bytes);
}
