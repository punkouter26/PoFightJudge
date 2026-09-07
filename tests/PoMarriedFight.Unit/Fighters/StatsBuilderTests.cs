using PoMarriedFight.Api.Features.Fighters;
using PoMarriedFight.Api.Features.Records;
using PoMarriedFight.Shared.Identifiers;
using PoMarriedFight.Shared.Models;

namespace PoMarriedFight.Unit.Fighters;

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
    public void Somebody_who_has_never_argued_has_a_record_of_nothing_rather_than_no_record()
    {
        var stats = FighterStatsBuilder.Build(Fighter(), []);

        stats.Fights.Should().Be(0);
        stats.WinRate.Should().Be(0);
        stats.LastFoughtAt.Should().BeNull();
        stats.Badges.Should().BeEmpty();
        stats.TopRival.Should().BeNull();
    }

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

    [Fact]
    public void Somebody_who_scores_the_same_every_time_is_consistent_and_somebody_who_swings_is_not()
    {
        var steady = FighterStatsBuilder.Build(Fighter(), [Result(1, true, 60), Result(2, true, 62), Result(3, true, 58)]);
        var wild = FighterStatsBuilder.Build(Fighter(), [Result(1, true, 10), Result(2, true, 95), Result(3, true, 30)]);

        steady.Consistency.Should().BeGreaterThan(wild.Consistency);
        steady.Consistency.Should().BeInRange(0, 1);
        wild.Consistency.Should().BeInRange(0, 1);
    }

    [Fact]
    public void The_form_line_is_the_recent_scores_oldest_first()
    {
        var results = Enumerable.Range(1, 12).Select(i => Result(i, true, 40 + i)).ToList();

        var stats = FighterStatsBuilder.Build(Fighter(), results);

        stats.Form.Should().HaveCount(FighterStatsBuilder.FormLength);
        stats.Form[^1].Should().Be(52, "the last score is the most recent one");
        stats.Form.Should().BeInAscendingOrder();
    }

    [Fact]
    public void Whoever_they_argue_with_most_is_the_rival_and_the_head_to_head_is_kept()
    {
        var stats = FighterStatsBuilder.Build(Fighter(),
        [
            Result(1, true, opponent: "SM"),
            Result(2, false, opponent: "SM"),
            Result(3, true, opponent: "SM"),
            Result(4, true, opponent: "ZZ"),
        ]);

        stats.TopRival!.Opponent.Should().Be("SM");
        stats.TopRival.Fights.Should().Be(3);
        stats.TopRival.Wins.Should().Be(2);
        stats.TopRival.IsAhead.Should().BeTrue();
    }

    [Fact]
    public void Badges_are_withheld_until_there_is_enough_to_mean_them()
    {
        var two = FighterStatsBuilder.Build(Fighter(), [Result(1, true, 90), Result(2, true, 92)]);

        two.Badges.Should().BeEmpty("a badge earned from one lucky night says nothing");

        var three = FighterStatsBuilder.Build(Fighter(), [Result(1, true, 90), Result(2, true, 92), Result(3, true, 88)]);
        three.Badges.Should().Contain("Usually right");
        three.Badges.Should().Contain(b => b.Contains("run of 3", StringComparison.Ordinal));
    }

    [Fact]
    public void A_habit_somebody_keeps_falling_into_is_named()
    {
        var straw = StyleSnapshot.Empty with { Fallacies = ["Straw man"] };
        var stats = FighterStatsBuilder.Build(Fighter(),
        [
            Result(1, true, style: straw),
            Result(2, false, style: straw),
            Result(3, true, style: StyleSnapshot.Empty),
        ]);

        stats.Badges.Should().Contain(b => b.Contains("straw man", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Watches_are_counted_as_well_as_fights_because_both_are_arguing()
    {
        var stats = FighterStatsBuilder.Build(Fighter(),
        [
            Result(1, true, mode: MatchMode.Watch),
            Result(2, false, mode: MatchMode.Watch),
        ]);

        stats.Fights.Should().Be(2);
        stats.Watches.Should().Be(2);
    }
}

/// <summary>A persona's record from the watches it has argued in.</summary>
public class ProfileStatsBuilderTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 1, 20, 0, 0, TimeSpan.Zero);

    private static WatchResultDto Result(int day, bool won, int score, double deflection, bool draw = false, string opponent = "KSH") =>
        new("MAH", MatchId.New(), Start.AddDays(day), "the thermostat", opponent, won, draw, score,
            new AdvancedStatsDto(20, 30, 40, 2, 50, 60, 70, deflection, 80, 5));

    [Fact]
    public void A_persona_that_has_never_argued_has_an_empty_record()
    {
        var record = ProfileStatsBuilder.Build("MAH", []);

        record.Matches.Should().Be(0);
        record.AverageStats.Should().Be(AdvancedStatsDto.Empty);
        record.LastSeenAt.Should().BeNull();
    }

    [Fact]
    public void The_record_is_the_rows_added_up_and_the_character_is_the_averages()
    {
        var record = ProfileStatsBuilder.Build("MAH",
        [
            Result(1, true, 70, deflection: 40),
            Result(2, false, 50, deflection: 60),
            Result(3, false, 60, deflection: 50, draw: true),
        ]);

        record.Matches.Should().Be(3);
        record.Wins.Should().Be(1);
        record.Draws.Should().Be(1);
        record.Losses.Should().Be(1);
        record.AverageScore.Should().Be(60);
        record.AverageStats.DeflectionCoefficient.Should().Be(50, "a persona that always deflects should show it in the number");
        record.LastSeenAt.Should().Be(Start.AddDays(3));
    }

    [Fact]
    public void The_persona_they_argue_with_most_is_the_rival()
    {
        var record = ProfileStatsBuilder.Build("MAH",
        [
            Result(1, true, 70, 40, opponent: "KSH"),
            Result(2, false, 50, 40, opponent: "KSH"),
            Result(3, true, 60, 40, opponent: "HRC"),
        ]);

        record.TopRival!.Opponent.Should().Be("KSH");
        record.TopRival.Fights.Should().Be(2);
    }
}
