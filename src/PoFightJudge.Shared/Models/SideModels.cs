using System.Text.Json.Serialization;

namespace PoFightJudge.Shared.Models;

/// <summary>Which side of the topic somebody argues. Serialized by name so the wire reads "For", not 0.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Stance>))]
public enum Stance
{
    For,
    Against,
}

/// <summary>
/// The two sides of a topic. Only the first side's stance is ever carried; the second is always the opposite, so the
/// two can never both be for it. A side means something only when there is a topic to be for or against.
/// </summary>
public static class Sides
{
    public static Stance Opposite(this Stance stance) => stance == Stance.For ? Stance.Against : Stance.For;

    /// <summary>"For" or "Against", as a person reads it on a card.</summary>
    public static string Label(this Stance stance) => stance == Stance.For ? "For" : "Against";

    /// <summary>
    /// The topic as a prompt reads it: with who is for it and who is against it when sides were chosen, and as it was
    /// when they were not. <paramref name="first"/> and <paramref name="second"/> are how the prompt names the two.
    /// </summary>
    public static string? Describe(string? topic, Stance? firstStance, string first, string second) =>
        string.IsNullOrWhiteSpace(topic) || firstStance is not { } stance
            ? topic
            : $"{topic.Trim()} ({first} is {Word(stance)} it; {second} is {Word(stance.Opposite())} it)";

    /// <summary>
    /// The side that won: the winner's own stance, the opposite of side one's when side two took it, and none for a draw
    /// or a match argued without sides.
    /// </summary>
    public static Stance? OfWinner(string? winnerId, string side1Id, string side2Id, Stance? side1Stance) =>
        side1Stance is not { } first || string.IsNullOrEmpty(winnerId) ? null
        : string.Equals(winnerId, side1Id, StringComparison.OrdinalIgnoreCase) ? first
        : string.Equals(winnerId, side2Id, StringComparison.OrdinalIgnoreCase) ? first.Opposite()
        : null;

    /// <summary>A side dealt at random, for a spun topic: nobody gets to pick the easy one.</summary>
    public static Stance Deal(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        return random.Next(2) == 0 ? Stance.For : Stance.Against;
    }

    /// <summary>The first side's stance if there is a topic to have one about, else none.</summary>
    public static Stance? For(string? topic, Stance stance) => string.IsNullOrWhiteSpace(topic) ? null : stance;

    private static string Word(Stance stance) => stance == Stance.For ? "FOR" : "AGAINST";
}
