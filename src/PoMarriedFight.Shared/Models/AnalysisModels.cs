using PoMarriedFight.Shared.Identifiers;

namespace PoMarriedFight.Shared.Models;

public sealed record FallacyDto(string Name, string Quote);

public sealed record ClaimDto(string Claim, string Verdict, string Note);

public sealed record EmotionProfileDto(int Calm, int Confident, int Frustrated, int Angry, int Anxious, int Amused);

public sealed record EmotionPointDto(int AtSeconds, string Dominant, int Intensity);

/// <summary>What the judge concluded about one fighter, from the recording and the transcript together.</summary>
public sealed record PlayerAssessmentDto(
    string Cefr,
    string CefrJustification,
    int GrammarErrorCount,
    IReadOnlyList<string> GrammarExamples,
    int VocabularySophistication,
    int Clarity,
    int Logic,
    IReadOnlyList<FallacyDto> Fallacies,
    int EvidenceUse,
    int RebuttalQuality,
    int Persuasiveness,
    int CorrectnessPercent,
    IReadOnlyList<ClaimDto> Claims,
    EmotionProfileDto Emotions,
    IReadOnlyList<EmotionPointDto> EmotionTimeline,
    string PeakMomentQuote,
    IReadOnlyList<string> ToneDescriptors,
    string PaceImpression,
    string EnergyImpression,
    string PitchVariationImpression,
    int Confidence,
    int Politeness,
    int Aggression,
    int Listening,
    string BestMomentQuote,
    string WorstMomentQuote,
    IReadOnlyList<string> CoachingTips);

public sealed record OverallVerdictDto(Speaker WinnerLogic, Speaker WinnerCorrect, Speaker Overall, IReadOnlyList<string> Reasons, string Summary);

/// <summary>The judge's structured output, before names and measurements are attached to it.</summary>
public sealed record JudgeOutputDto(PlayerAssessmentDto Player1, PlayerAssessmentDto Player2, JudgeOverallDto Overall);

/// <summary>Both assessments from the one call that carries the recording; the ruling follows separately.</summary>
public sealed record BothAssessmentsDto(PlayerAssessmentDto Player1, PlayerAssessmentDto Player2);

public sealed record JudgeOverallDto(string WinnerLogic, string WinnerCorrect, string Overall, IReadOnlyList<string> Reasons, string Summary);

public sealed record PlayerReportDto(string Name, PlayerMetricsDto Metrics, PlayerAssessmentDto Assessment);

/// <summary>The whole report on one fight: what was measured, what the judge made of it, and how it was ruled.</summary>
public sealed record AnalysisReportDto(
    MatchId MatchId,
    string Topic,
    PlayerReportDto Player1,
    PlayerReportDto Player2,
    OverallVerdictDto Overall,
    string? HostVerdict,
    bool SpeakerMappingFlagged,
    string SpeakerMappingNote,
    double AnalysisSeconds,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<HighlightDto>? Highlights = null);

/// <summary>A quotable moment, playable as a clip. Offsets are seconds into the recording of the room.</summary>
public sealed record HighlightDto(Speaker Speaker, string Label, string Quote, double StartSeconds, double EndSeconds);

/// <summary>What is served when a fight's analysis is asked for.</summary>
public sealed record AnalysisResponse(AnalysisStatus Status, string? Error, AnalysisReportDto? Report);

/// <summary>What was measured rather than judged: countable things about how one fighter talked.</summary>
public sealed record PlayerMetricsDto(
    int Words,
    double SpeakingSeconds,
    double WordsPerMinute,
    double TalkShare,
    int Fillers,
    double FillersPer100,
    int Interruptions,
    int Questions,
    double AverageWordLength,
    double FleschKincaidGrade,
    double LongestMonologueSeconds,
    int UniqueWords,
    double TypeTokenRatio);

/// <summary>One transcribed word, once its speaker label has been resolved to an actual side of the fight.</summary>
public sealed record MappedWord(string Text, Speaker Speaker, double Start, double End);

/// <summary>
/// A transcript whose speaker labels have been resolved. <paramref name="Flagged"/> marks a mapping the pipeline is
/// not confident in, so the report can say so rather than quietly attributing words to the wrong person.
/// </summary>
public sealed record MappedTranscript(IReadOnlyList<MappedWord> Words, bool Flagged, string Note);
