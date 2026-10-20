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
    public void A_bout_moves_both_sides_by_the_same_amount_once_and_a_draw_between_equals_or_a_bout_with_yourself_moves_nothing()
    {
        var ratings = EloRatings.Compute([Row("AL", "SM", 1, won: true)]);
        ratings["AL"].Should().Be(EloRatings.Start + 16);
        ratings["SM"].Should().Be(EloRatings.Start - 16, "the opponent is rated from the winner's row when they have none of their own");

        // A two-player fight is counted once though both sides have a row.
        var match = MatchId.New();
        var both = EloRatings.Compute([Row("AL", "SM", 1, won: true, match: match), Row("SM", "AL", 1, won: false, match: match)]);
        both["AL"].Should().Be(EloRatings.Start + 16);
        both["SM"].Should().Be(EloRatings.Start - 16);

        var draw = EloRatings.Compute([Row("AL", "SM", 1, won: false, draw: true)]);
        draw["AL"].Should().Be(EloRatings.Start);
        draw["SM"].Should().Be(EloRatings.Start);

        EloRatings.Compute([Row("AL", "al", 1, won: true)]).Should().BeEmpty("somebody argued against themselves is not a bout");
    }

    [Fact]
    public void Beating_somebody_rated_higher_is_worth_more_and_the_order_is_the_order_they_argued_in()
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

        // Not the order the rows were read in.
        List<FighterResultDto> rows = [Row("AL", "SM", 1, won: true), Row("AL", "SM", 2, won: false), Row("SM", "JO", 3, won: true)];
        EloRatings.Compute(rows).Should().BeEquivalentTo(EloRatings.Compute([.. Enumerable.Reverse(rows)]));
    }
}
