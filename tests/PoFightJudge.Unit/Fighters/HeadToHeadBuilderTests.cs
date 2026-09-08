using PoFightJudge.Api.Features.Fighters;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Unit.Fighters;

/// <summary>
/// The series between two people. Computed from the same rows their records are, so it can never disagree with
/// either page — and a meeting is two rows, one per person, which is what makes both scores available at once.
/// </summary>
public class HeadToHeadBuilderTests
{
    private static readonly DateTimeOffset Night = new(2026, 9, 1, 20, 0, 0, TimeSpan.Zero);
    private static readonly FighterDto Alex = new("AB", "Alex", Night, Night);
    private static readonly FighterDto Sam = new("CD", "Sam", Night, Night);

    private static FighterResultDto Row(string tag, string opponent, MatchId match, bool won, int score, int day, bool draw = false) =>
        new(tag, "u", match, MatchMode.Fight, Night.AddDays(day), "the bins", opponent, won, draw, score, StyleSnapshot.Empty);

    [Fact]
    public void The_series_counts_only_the_fights_between_these_two()
    {
        var one = MatchId.New();
        var two = MatchId.New();
        var elsewhere = MatchId.New();

        var h2h = HeadToHeadBuilder.Build(
            Alex,
            Sam,
            [Row("AB", "CD", one, won: true, 70, 1), Row("AB", "CD", two, won: false, 40, 2), Row("AB", "EF", elsewhere, won: true, 90, 3)],
            [Row("CD", "AB", one, won: false, 30, 1), Row("CD", "AB", two, won: true, 60, 2)]);

        h2h.Meetings.Should().Be(2, "the fight against EF is not part of this series");
        h2h.Wins.Should().Be(1);
        h2h.Losses.Should().Be(1);
        h2h.IsAhead.Should().BeFalse("level is not ahead");
        h2h.AverageScore.Should().Be(55);
        h2h.OpponentAverageScore.Should().Be(45);
    }

    /// <summary>
    /// Both scores come from both sides' rows, joined on the match. Reading one side only would show one number
    /// beside a zero and call it a comparison.
    /// </summary>
    [Fact]
    public void Each_meeting_carries_both_scores()
    {
        var match = MatchId.New();

        var h2h = HeadToHeadBuilder.Build(
            Alex,
            Sam,
            [Row("AB", "CD", match, won: true, 72, 1)],
            [Row("CD", "AB", match, won: false, 28, 1)]);

        var meeting = h2h.Recent.Should().ContainSingle().Subject;
        meeting.Score.Should().Be(72);
        meeting.OpponentScore.Should().Be(28);
        meeting.Winner.Should().Be("AB");
    }

    [Fact]
    public void A_draw_is_neither_a_win_nor_a_loss_and_names_nobody()
    {
        var match = MatchId.New();

        var h2h = HeadToHeadBuilder.Build(
            Alex,
            Sam,
            [Row("AB", "CD", match, won: false, 50, 1, draw: true)],
            [Row("CD", "AB", match, won: false, 50, 1, draw: true)]);

        h2h.Wins.Should().Be(0);
        h2h.Losses.Should().Be(0);
        h2h.Draws.Should().Be(1);
        h2h.Recent.Single().IsDraw.Should().BeTrue();
    }

    [Fact]
    public void Two_people_who_have_never_argued_say_so_rather_than_reading_as_nil_nil()
    {
        var h2h = HeadToHeadBuilder.Build(Alex, Sam, [], []);

        h2h.NeverMet.Should().BeTrue();
        h2h.AverageScore.Should().Be(0, "an average of nothing is not NaN on a page");
        h2h.Recent.Should().BeEmpty();
    }

    [Fact]
    public void The_meetings_read_newest_first_and_the_list_is_capped()
    {
        var rows = Enumerable.Range(0, HeadToHeadBuilder.RecentCount + 4)
            .Select(day => Row("AB", "CD", MatchId.New(), won: true, 60, day))
            .ToList();

        var h2h = HeadToHeadBuilder.Build(Alex, Sam, rows, []);

        h2h.Meetings.Should().Be(rows.Count, "every meeting is counted even when it is not printed");
        h2h.Recent.Should().HaveCount(HeadToHeadBuilder.RecentCount);
        h2h.Recent.Should().BeInDescendingOrder(m => m.At);
    }

    /// <summary>A tag is normalised everywhere it is written, but a comparison that assumed it must not be case-blind.</summary>
    [Fact]
    public void The_opponents_tag_matches_however_it_was_cased()
    {
        var match = MatchId.New();

        var h2h = HeadToHeadBuilder.Build(
            Alex,
            Sam,
            [Row("AB", "cd", match, won: true, 70, 1)],
            [Row("CD", "AB", match, won: false, 30, 1)]);

        h2h.Meetings.Should().Be(1);
    }
}
