using System.Text.Json;
using PoMarriedFight.Shared;
using PoMarriedFight.Shared.Identifiers;

namespace PoMarriedFight.Unit.Shared;

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
    public void ProfileId_accepts_the_SELF_sentinel_but_not_junk()
    {
        ProfileId.TryFrom(SelfPlayer.Initials, out var self).Should().BeTrue();
        SelfPlayer.Is(self).Should().BeTrue();
        SelfPlayer.InMatch(ProfileId.From("KDH"), self).Should().BeTrue();
        SelfPlayer.InMatch(ProfileId.From("KDH"), ProfileId.From("MLT")).Should().BeFalse();

        ProfileId.TryFrom(string.Empty, out _).Should().BeFalse();
        ProfileId.TryFrom("!!!", out _).Should().BeFalse();
        ProfileId.From("TOOLONG").Value.Should().Be("TOOL", "normalisation truncates to four characters rather than rejecting, like PoArgueJudge Initials");
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

    [Fact]
    public void MatchId_is_a_compact_guid_and_round_trips()
    {
        var id = MatchId.New();

        id.Value.Should().HaveLength(32).And.MatchRegex("^[0-9a-f]{32}$");
        MatchId.New().Should().NotBe(id);
        MatchId.TryFrom("not a guid!", out _).Should().BeFalse();
        MatchId.From(id.Value).Should().Be(id);
    }

    [Fact]
    public void Ids_serialise_as_bare_json_strings()
    {
        var profile = ProfileId.From("KDH");
        var fighter = FighterId.From("MLT");
        var match = MatchId.New();

        JsonSerializer.Serialize(profile).Should().Be("\"KDH\"");
        JsonSerializer.Serialize(fighter).Should().Be("\"MLT\"");
        JsonSerializer.Serialize(match).Should().Be($"\"{match.Value}\"");
        JsonSerializer.Deserialize<ProfileId>("\"kdh\"").Should().Be(profile, "JSON input is normalised too");
        JsonSerializer.Deserialize<FighterId>("\"mlt\"").Should().Be(fighter);
        JsonSerializer.Deserialize<MatchId>($"\"{match.Value}\"").Should().Be(match);
    }

    [Fact]
    public void Ids_are_parsable_for_route_binding()
    {
        ProfileId.TryParse("kdh", null, out var profile).Should().BeTrue();
        profile.Value.Should().Be("KDH");
        FighterId.TryParse("zz", null, out var fighter).Should().BeTrue();
        fighter.Value.Should().Be("ZZ");
        MatchId.TryParse("nope", null, out _).Should().BeFalse();
        ProfileId.Parse("mlt", null).Value.Should().Be("MLT");
    }
}
