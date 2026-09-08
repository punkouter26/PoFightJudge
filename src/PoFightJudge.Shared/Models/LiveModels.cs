using PoFightJudge.Shared.Identifiers;

namespace PoFightJudge.Shared.Models;

/// <summary>
/// Method names on the live hub, shared so the browser and the server cannot drift apart on a spelling. The first
/// three are what the room calls; the rest are what it is told.
/// </summary>
public static class LiveHubContract
{
    public const string JoinFight = "JoinFight";
    public const string AudioIn = "AudioIn";
    public const string EndFight = "EndFight";

    public const string AudioOut = "AudioOut";
    public const string AudioClear = "AudioClear";
    public const string Caption = "Caption";
    public const string Snapshot = "Snapshot";
    public const string Ended = "Ended";
    public const string Error = "Error";
}

/// <summary>
/// Body of a request to start a fight: who is hosting, both fighter tags, and what they have agreed to argue about.
/// Both tags are required, because a fight goes on both their records and a record needs a name.
/// </summary>
public sealed record CreateFightRequest(
    HostPersonaId Persona = HostPersonaId.Referee,
    string Player1Tag = "",
    string Player2Tag = "",
    string? Topic = null,
    ProfileRole Player1Role = ProfileRole.Husband,
    ProfileRole Player2Role = ProfileRole.Wife);

/// <summary>The fight that was started. Its id is what the browser joins the hub with.</summary>
public sealed record CreateFightResponse(MatchId MatchId);

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
