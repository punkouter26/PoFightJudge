using System.Text.Json;
using PoFightJudge.Shared;
using PoFightJudge.Shared.Identifiers;

namespace PoFightJudge.Unit.Shared;

/// <summary>
/// The three ids are Vogen value objects: normalised on construction, validated once, serialised as bare strings,
/// and parsable so Minimal APIs bind them straight from a route segment.
/// </summary>
/// <summary>
/// The three ids are Vogen value objects: normalised on construction, validated once, serialised as bare strings,
/// and parsable so Minimal APIs bind them straight from a route segment.
/// </summary>
public class IdentifierTests
{
    [Fact]
    public void ProfileId_upper_cases_and_is_the_initials()
    {
        var id = ProfileId.From("kdh");

        id.Value.Should().Be("KDH");
        id.ToString().Should().Be("KDH");
        ProfileId.From(" k.d.h ").Should().Be(id, "punctuation and whitespace are stripped like PoArgueJudge's Initials");
    }

    [Fact]
    public void FighterId_is_an_arcade_tag_of_one_to_three_alphanumerics()
    {
        FighterId.From("mlt").Value.Should().Be("MLT");
        FighterId.From("a1").Value.Should().Be("A1");
        FighterId.From("SELF").Value.Should().Be("SEL", "tags truncate to three characters — SELF is a WATCH concept, never a fighter");
        FighterId.TryFrom("K-D", out var kd).Should().BeTrue();
        kd.Value.Should().Be("KD", "non-alphanumerics are dropped before validation");
        FighterId.TryFrom("---", out _).Should().BeFalse();
    }
}
