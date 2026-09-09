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
    /// Accept a word-level transcript produced in the browser instead of calling the transcribe model. On since
    /// T85, when the browser half was finally written: it sits behind the host's own live captions and in front of
    /// the paid diarization, so the only fight it changes is one whose captions came out too thin to judge on —
    /// which used to be the only fight that paid. What arrives is bounded by <see cref="ClientTranscript"/> before
    /// it is stored, and the source every run actually used is logged.
    /// </summary>
    public bool AcceptClientTranscript { get; set; } = true;

    /// <summary>
    /// How long to wait for that transcript to arrive, on top of the time the recording's upload already takes.
    /// </summary>
    /// <remarks>
    /// Zero by default, which means "take it if it is already there". The browser posts it as the fight ends —
    /// before it navigates away, which is the same moment the analysis is queued — and the upload of the recording
    /// then runs ahead of this check, so in practice it has arrived. Waiting longer buys the rare late post at the
    /// cost of delaying every fight, including the ones whose live captions were going to win anyway.
    ///
    /// It is also the one knob here that can stop the pipeline dead: the wait runs on <c>TimeProvider</c>, so a
    /// non-zero value against a clock nobody advances never returns. That is a test concern rather than a
    /// production one, but it is the reason the default is the value that does not wait.
    /// </remarks>
    public double ClientTranscriptWaitSeconds { get; set; }

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
