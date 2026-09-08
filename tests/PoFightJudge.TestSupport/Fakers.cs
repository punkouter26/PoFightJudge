using Bogus;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.TestSupport;

/// <summary>
/// Deterministic realistic data for tests and the Development fakes: seeded once, so a run is reproducible and a
/// snapshot never churns. Domain fakers (profiles, transcripts, results) join here as their DTOs land.
/// </summary>
public static class Fakers
{
    public const int Seed = 20260906;

    private static readonly string[] Topics =
    [
        "Whose turn is the dishwasher", "Thermostat wars", "The in-laws at Christmas", "Screen time after nine",
        "Who forgot the anniversary", "The correct way to load a car", "Leaving the lights on", "Weekend plans, again",
        "The thermostat, part two", "Whose family we visit first",
    ];

    private static readonly string[] Openers =
    [
        "Look, the thing is,", "Okay so,", "Honestly?", "Here's what actually happened:", "Can I just say,",
        "Let me finish —", "Right, so,", "The point is,",
    ];

    /// <summary>A fresh seeded faker; call once per test (or per fake) and reuse it for stable sequences.</summary>
    public static Faker New(int seed = Seed) => new("en") { Random = new Randomizer(seed) };

    /// <summary>1–3 upper-case alphanumerics that pass <see cref="Initials.IsValid"/>.</summary>
    public static string Tag(Faker f) => Initials.Normalize(f.Random.String2(f.Random.Int(1, 3), "ABCDEFGHJKLMNPQRSTUVWXYZ"));

    /// <summary>Two different tags.</summary>
    public static (string First, string Second) TagPair(Faker f)
    {
        var first = Tag(f);
        string second;
        do
        {
            second = Tag(f);
        }
        while (string.Equals(first, second, StringComparison.Ordinal));
        return (first, second);
    }

    public static string Topic(Faker f) => f.PickRandom(Topics);

    public static string FirstName(Faker f) => f.Name.FirstName();

    /// <summary>One spoken line the way a person argues: an opener, then a clause or two.</summary>
    public static string Line(Faker f) => $"{f.PickRandom(Openers)} {f.Lorem.Sentence(f.Random.Int(6, 14))}";

    /// <summary>A short paragraph for persona fields (likes, philosophy…).</summary>
    public static string Blurb(Faker f) => f.Lorem.Sentence(f.Random.Int(8, 16));
}
