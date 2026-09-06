using PoMarriedFight.Shared.Models;

namespace PoMarriedFight.Client.Components;

/// <summary>
/// Reads the temperature of an argument from the moods its lines carried. It is a display value, not a score: the
/// judge rules on the words, this only tells the room how hot it has become.
/// </summary>
public static class TensionCalculator
{
    /// <summary>A slap does not change what was said, but it does change the room.</summary>
    private const int SlapHeat = 12;

    /// <summary>Where each mood in the closed wire set sits on the scale.</summary>
    private static readonly Dictionary<string, int> Heat = new(StringComparer.Ordinal)
    {
        ["calm"] = 5,
        ["apologetic"] = 9,
        ["humble"] = 12,
        ["sad"] = 20,
        ["smug"] = 35,
        ["passive-aggressive"] = 44,
        ["sarcastic"] = 52,
        ["frustrated"] = 60,
        ["angry"] = 74,
        ["hostile"] = 84,
        ["hateful"] = 93,
        ["furious"] = 100,
    };

    /// <summary>
    /// The temperature now, 0–100. Later lines weigh more than earlier ones: an argument that cooled off must not
    /// still read red, and one that has just boiled over has to show it on the line that did it.
    /// </summary>
    public static int Tension(IReadOnlyList<WatchRoundDto> rounds, bool slapped = false)
    {
        ArgumentNullException.ThrowIfNull(rounds);
        if (rounds.Count == 0)
        {
            return 0;
        }

        double weighted = 0;
        double weights = 0;
        for (var i = 0; i < rounds.Count; i++)
        {
            var weight = i + 1;
            weighted += HeatOf(rounds[i].Mood) * weight;
            weights += weight;
        }

        var value = (weighted / weights) + (slapped ? SlapHeat : 0);
        return Math.Clamp((int)Math.Round(value, MidpointRounding.AwayFromZero), 0, 100);
    }

    /// <summary>The word for a temperature, so the meter says something a person can read at a glance.</summary>
    public static string Level(int tension) =>
        tension < 25 ? "Civil"
        : tension < 50 ? "Testy"
        : tension < 75 ? "Heated"
        : "Nuclear";

    /// <summary>An unrecognised mood is mapped the same way the server maps it, rather than reading as ice cold.</summary>
    private static int HeatOf(string? mood) => Heat[WatchTurns.NormalizeMood(mood)];
}
