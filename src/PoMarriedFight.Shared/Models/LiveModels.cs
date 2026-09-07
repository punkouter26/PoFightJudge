namespace PoMarriedFight.Shared.Models;

/// <summary>
/// One line of live caption. <paramref name="Final"/> marks the end of a turn: everything before it is the same
/// sentence still being spoken, so the room replaces rather than appends.
/// </summary>
public sealed record CaptionDto(Speaker Speaker, string Text, bool Final);

/// <summary>
/// One word of a transcript, with the speaker label it was attributed to and where it sits in the recording.
/// Labels keep the <c>label_n</c> shape a diarizer produces, so the same mapper reads both sources.
/// </summary>
public sealed record TranscriptWord(string Text, string Label, double Start, double End);

/// <summary>A whole transcript: the words in order, and the plain text they spell out.</summary>
public sealed record TranscriptDto(string Text, IReadOnlyList<TranscriptWord> Words);
