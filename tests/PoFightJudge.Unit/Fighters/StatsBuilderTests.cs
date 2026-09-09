using PoFightJudge.Api.Features.Fighters;
using PoFightJudge.Api.Features.Records;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Unit.Fighters;

/// <summary>
/// A record computed on read, never accumulated. Re-reading a fight corrects it; deleting one removes it.
/// </summary>
/// <summary>
/// A record computed on read, never accumulated. Re-reading a fight corrects it; deleting one removes it.
/// </summary>
public class FighterStatsBuilderTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 1, 20, 0, 0, TimeSpan.Zero);

    private static FighterDto Fighter(string tag = "AL") => new(tag, "Alex", Start.AddYears(-1), Start);

    private static FighterResultDto Result(
        int day,
        bool won,
        int score = 50,
        string opponent = "SM",
        bool draw = false,
        MatchMode mode = MatchMode.Fight,
        StyleSnapshot? style = null) =>
        new("AL", "u", MatchId.New(), mode, Start.AddDays(day), "the thermostat", opponent, won, draw, score, style ?? StyleSnapshot.Empty);

    [Fact]
    public void Wins_losses_and_draws_add_up_to_the_fights_that_happened()
    {
        var stats = FighterStatsBuilder.Build(Fighter(), [Result(1, true), Result(2, false), Result(3, false, draw: true)]);

        stats.Fights.Should().Be(3);
        stats.Wins.Should().Be(1);
        stats.Losses.Should().Be(1);
        stats.Draws.Should().Be(1);
        (stats.Wins + stats.Losses + stats.Draws).Should().Be(stats.Fights);
        stats.WinRate.Should().BeApproximately(1.0 / 3, 0.01);
    }

    [Fact]
    public void A_run_is_counted_back_from_the_most_recent_and_a_draw_ends_it()
    {
        FighterStatsBuilder.Build(Fighter(), [Result(1, false), Result(2, true), Result(3, true)]).Streak.Should().Be(2);
        FighterStatsBuilder.Build(Fighter(), [Result(1, true), Result(2, false), Result(3, false)]).Streak.Should().Be(-2);
        FighterStatsBuilder.Build(Fighter(), [Result(1, true), Result(2, true), Result(3, false, draw: true)]).Streak
            .Should().Be(0, "neither side of a draw is on a run");
    }
}
