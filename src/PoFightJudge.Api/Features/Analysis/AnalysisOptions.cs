using PoFightJudge.Shared.Configuration;

namespace PoFightJudge.Api.Features.Analysis;

/// <summary>Throughput and source knobs for the post-fight pipeline. Bound from <c>PoFightJudge:Analysis</c>.</summary>
public sealed class AnalysisOptions
{
    public const string Section = ConfigKeys.Analysis.Section;

    /// <summary>
    /// How many finished fights may be analysed at once. Kept small: each job holds a whole recording in memory,
    /// and two fights ending together should not queue behind each other for the length of the first.
    /// </summary>
    public int MaxParallelAnalyses { get; set; } = 2;

    /// <summary>
    /// Use the transcript the orchestrator assembled from the host's own captions and skip the diarization call.
    /// On by default: the fight already paid for those captions, so transcribing the same audio a second time is
    /// the largest avoidable cost here. Anything missing or too thin falls back to the transcribe model.
    /// </summary>
    public bool UseLiveCaptionTranscript { get; set; } = true;

    /// <summary>
    /// Below this many words a caption transcript is too thin to judge on and the transcribe model runs instead. A
    /// fight where the captions dropped out is worth paying to recover.
    /// </summary>
    public int MinCaptionTranscriptWords { get; set; } = 30;

    /// <summary>
    /// Accept a word-level transcript produced in the browser instead of calling the transcribe model. Off by
    /// default: that path is best-effort, and the server model stays the source of truth until it is proven.
    /// </summary>
    public bool AcceptClientTranscript { get; set; }

    /// <summary>
    /// How long to wait for that transcript to arrive. The browser posts it as the fight ends, which is the same
    /// moment the analysis is queued, so the upload usually covers the wait. Bounded, so a browser that closed
    /// mid-fight costs a few seconds rather than the analysis.
    /// </summary>
    public double ClientTranscriptWaitSeconds { get; set; } = 3;
}
