using PoMarriedFight.Api.Features.Fighters;
using PoMarriedFight.Shared.Identifiers;
using PoMarriedFight.Shared.Models;

namespace PoMarriedFight.Unit.Fighters;

public class FighterTests
{
    private static readonly DateTimeOffset Created = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_fighter_starts_named_after_its_tag_because_nobody_writes_one()
    {
        var fighter = Fighter.Create(FighterId.From("kd"), Created);

        fighter.Tag.Should().Be("KD", "a tag is normalised once and never trusted from input again");
        fighter.DisplayName.Should().Be("KD", "until they choose a name, the tag is the name");
        fighter.CreatedAt.Should().Be(Created);
        fighter.LastSeenAt.Should().Be(Created);
        Fighter.Create(FighterId.From("AB"), Created, "  Alex  ").DisplayName.Should().Be("Alex");
    }

    [Fact]
    public void Only_the_display_name_can_be_edited_and_a_blank_one_falls_back_to_the_tag()
    {
        var fighter = Fighter.Create(FighterId.From("AB"), Created, "Alex");

        fighter.Rename("Alexandra");
        fighter.DisplayName.Should().Be("Alexandra");
        fighter.Tag.Should().Be("AB", "renaming must never move a person off their own record");

        fighter.Rename("   ");
        fighter.DisplayName.Should().Be("AB", "a nameless fighter is shown by tag rather than as a blank");
    }

    [Fact]
    public void Being_seen_moves_the_roster_forward_but_never_backwards()
    {
        var fighter = Fighter.Create(FighterId.From("AB"), Created);

        fighter.Seen(Created.AddDays(3));
        fighter.LastSeenAt.Should().Be(Created.AddDays(3));

        fighter.Seen(Created.AddDays(1));
        fighter.LastSeenAt.Should().Be(Created.AddDays(3), "an out-of-order write must not rewind the roster");
        fighter.CreatedAt.Should().Be(Created, "first seen is history and never moves");
    }

    [Fact]
    public void A_fighter_round_trips_through_its_dto()
    {
        var fighter = Fighter.Create(FighterId.From("CD"), Created, "Casey");
        fighter.Seen(Created.AddHours(5));

        var back = Fighter.Rehydrate(fighter.ToDto());

        back.ToDto().Should().Be(fighter.ToDto());
        Fighter.Rehydrate(new FighterDto("EF", string.Empty, Created, Created)).DisplayName.Should().Be("EF", "a row written before display names still shows something");
    }
    [Fact]
    public void A_fighter_argues_as_the_husband_until_they_say_otherwise_and_the_choice_travels_with_them()
    {
        // 2P has no roles of its own; the choice exists so that the persona read from their fights can take a seat
        // in CPU and 1P, which pit a husband against a wife.
        var fighter = Fighter.Create(FighterId.From("KKK"), Created);
        fighter.Role.Should().Be(ProfileRole.Husband);

        fighter.ArgueAs(ProfileRole.Wife);
        fighter.Role.Should().Be(ProfileRole.Wife);
        fighter.ToDto().Role.Should().Be(ProfileRole.Wife, "the roster carries it");
        Fighter.Rehydrate(fighter.ToDto()).Role.Should().Be(ProfileRole.Wife, "and it survives storage");
        Fighter.Create(FighterId.From("LLL"), Created, role: ProfileRole.Wife).Role.Should().Be(ProfileRole.Wife);
    }
}
