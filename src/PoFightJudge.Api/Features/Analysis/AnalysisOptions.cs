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
    /// Off by default: every fight goes to <c>gemini-3.5-transcribe</c> for a verbatim, speaker-diarized transcript,
    /// which keeps the fillers and false starts the captions smooth over. Turning it on saves that call, and anything
    /// missing or too thin still falls back to the transcribe model.
    /// </summary>
    public bool UseLiveCaptionTranscript { get; set; }

    /// <summary>
    /// Below this many words a caption transcript is too thin to judge on and the transcribe model runs instead. A
    /// fight where the captions dropped out is worth paying to recover.
    /// </summary>
    public int MinCaptionTranscriptWords { get; set; } = 30;

    /// <summary>
    /// Upload the two assessments' shared prefix — the instructions, the recording and the session data — once as a
    /// cachedContents resource, so both read it by name and can go out together. On by default: it is the same
    /// bytes either way, and the alternative is hoping the provider's implicit cache is still warm while the calls
    /// queue behind each other to give it the chance. A refused cache falls back to sending the prefix inline.
    /// </summary>
    public bool JudgeContextCache { get; set; } = true;

    /// <summary>
    /// How long the cache is allowed to live. It is deleted as soon as the ruling is written, so this is the
    /// backstop for an analysis that died halfway rather than the plan — and it is billed by storage time, which is
    /// why it is minutes rather than hours.
    /// </summary>
    public int JudgeCacheTtlSeconds { get; set; } = 600;

    /// <summary>
    /// How long after a fight ended its unfinished analysis is still worth picking back up on startup. Past this it
    /// is failed and says so: a day-old spinner is not something anybody is still watching, and the recording it
    /// would need may not be in storage any more.
    /// </summary>
    public int ResumeMaxAgeHours { get; set; } = 24;
}
