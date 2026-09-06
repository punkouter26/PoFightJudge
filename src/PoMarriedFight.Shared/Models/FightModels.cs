using System.Text.Json.Serialization;
namespace PoMarriedFight.Shared.Models;

/// <summary>Which engine produced a match. Stored on every <c>Matches</c> row; drives history badges and the leaderboard tabs.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MatchMode>))]
public enum MatchMode
{
    Watch,
    Fight,
}

/// <summary>Live fight phases, driven only by the host's tool calls and the tick nudges.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SessionPhase>))]
public enum SessionPhase
{
    Intro,
    Setup,
    Debate,
    Probe,
    Verdict,
    Done,
}

/// <summary>Lifecycle of a match row after the live part is over.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SessionStatus>))]
public enum SessionStatus
{
    Live,
    Analyzing,
    Ready,
    Failed,
}

[JsonConverter(typeof(JsonStringEnumConverter<Speaker>))]
public enum Speaker
{
    Host,
    Player1,
    Player2,
}

/// <summary>What a stored turn is. <see cref="Round"/> is a WATCH line; the rest come from a live fight.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TurnKind>))]
public enum TurnKind
{
    Talk,
    Probe,
    Interrupt,
    Verdict,
    Round,
}

[JsonConverter(typeof(JsonStringEnumConverter<AnalysisStatus>))]
public enum AnalysisStatus
{
    Queued,
    Diarizing,
    Judging,
    Ready,
    Failed,
}

/// <summary>
/// Arcade-style tags: 1–3 letters or digits, upper case. Fighter identity and the FightResults partition key are these
/// tags, so they are normalised once (here) and never trusted from the model afterwards.
/// </summary>
public static class Initials
{
    public const int MaxLength = 3;

    /// <summary>Upper-cases, drops anything that is not a letter or digit, and caps the length. Never null.</summary>
    public static string Normalize(string? value) =>
        new([.. (value ?? string.Empty).ToUpperInvariant().Where(char.IsAsciiLetterOrDigit).Take(MaxLength)]);

    public static bool IsValid(string? value) => Normalize(value).Length > 0;

    /// <summary>Both tags must be usable and different — stats and speaker attribution key on them.</summary>
    public static bool ArePair(string? first, string? second) =>
        IsValid(first) && IsValid(second) && !Normalize(first).Equals(Normalize(second), StringComparison.Ordinal);
}
