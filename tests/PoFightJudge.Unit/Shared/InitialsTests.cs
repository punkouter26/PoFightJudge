using PoFightJudge.Shared.Models;

namespace PoFightJudge.Unit.Shared;

public class InitialsTests
{
    [Fact]
    public void Normalize_upper_cases_strips_non_alphanumerics_and_caps_at_three()
    {
        Initials.Normalize(" k.d.h ").Should().Be("KDH");
        Initials.Normalize("kevin").Should().Be("KEV");
        Initials.Normalize("a-1").Should().Be("A1");
        Initials.Normalize(null).Should().BeEmpty();
        Initials.Normalize("!!!").Should().BeEmpty();
    }

    [Fact]
    public void IsValid_needs_at_least_one_character_after_normalisation()
    {
        Initials.IsValid("k").Should().BeTrue();
        Initials.IsValid("   ").Should().BeFalse();
        Initials.IsValid("--").Should().BeFalse();
    }

    [Fact]
    public void ArePair_requires_two_distinct_usable_tags()
    {
        Initials.ArePair("KDH", "MLT").Should().BeTrue();
        Initials.ArePair("kdh", "K.D.H").Should().BeFalse("they normalise to the same tag");
        Initials.ArePair("KDH", "").Should().BeFalse();
        Initials.ArePair(null, "MLT").Should().BeFalse();
    }
}
