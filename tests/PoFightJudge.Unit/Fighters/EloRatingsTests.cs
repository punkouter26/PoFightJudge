using PoFightJudge.Api.Features.Records;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Unit.Fighters;

/// <summary>A rating replayed from the result rows on every read, so a deleted fight takes its points with it.</summary>
public class EloRatingsTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 1, 20, 0, 0, TimeSpan.Zero);

    private static FighterResultDto Row(string tag, string opponent, int day, bool won, bool draw = false, MatchId? match = null) =>
        new(tag, "u", match ?? MatchId.New(), MatchMode.Fight, Start.AddDays(day), "the thermostat", opponent, won, draw, 50, StyleSnapshot.Empty);

    [Fact]
    public void A_win_moves_both_sides_by_the_same_amount()
    {
        var ratings = EloRatings.Compute([Row("AL", "SM", 1, won: true)]);

        ratings["AL"].Should().Be(EloRatings.Start + 16);
        ratings["SM"].Should().Be(EloRatings.Start - 16, "the opponent is rated from the winner's row when they have none of their own");
    }

    [Fact]
    public void A_two_player_fight_is_counted_once_though_both_sides_have_a_row()
    {
        var match = MatchId.New();
        var ratings = EloRatings.Compute([Row("AL", "SM", 1, won: true, match: match), Row("SM", "AL", 1, won: false, match: match)]);

        ratings["AL"].Should().Be(EloRatings.Start + 16);
        ratings["SM"].Should().Be(EloRatings.Start - 16);
    }

    [Fact]
    public void A_draw_between_equals_changes_nothing()
    {
        var ratings = EloRatings.Compute([Row("AL", "SM", 1, won: false, draw: true)]);

        ratings["AL"].Should().Be(EloRatings.Start);
        ratings["SM"].Should().Be(EloRatings.Start);
    }

    [Fact]
    public void Beating_somebody_rated_higher_is_worth_more_than_beating_somebody_rated_lower()
    {
        // SM beats JO three times, so SM is up and JO is down. Then two newcomers: AL beats SM, KY beats JO.
        var ratings = EloRatings.Compute(
        [
            Row("SM", "JO", 1, won: true),
            Row("SM", "JO", 2, won: true),
            Row("SM", "JO", 3, won: true),
            Row("AL", "SM", 4, won: true),
            Row("KY", "JO", 4, won: true),
        ]);

        (ratings["AL"] - EloRatings.Start).Should().BeGreaterThan(ratings["KY"] - EloRatings.Start);
    }

    [Fact]
    public void Somebody_argued_against_themselves_is_not_a_bout()
    {
        EloRatings.Compute([Row("AL", "al", 1, won: true)]).Should().BeEmpty();
    }

    [Fact]
    public void The_order_is_the_order_they_argued_in_not_the_order_the_rows_were_read()
    {
        List<FighterResultDto> rows = [Row("AL", "SM", 1, won: true), Row("AL", "SM", 2, won: false), Row("SM", "JO", 3, won: true)];

        EloRatings.Compute(rows).Should().BeEquivalentTo(EloRatings.Compute([.. Enumerable.Reverse(rows)]));
    }
}
