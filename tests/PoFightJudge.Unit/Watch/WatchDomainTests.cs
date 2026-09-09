using PoFightJudge.Api.Features.Watch;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Unit.Watch;

public class WatchMatchTests
{
    private static readonly DateTimeOffset Ended = new(2026, 9, 6, 20, 41, 0, TimeSpan.Zero);

    private static WatchRound Round(string speaker = WatchRound.Husband) => new(speaker, "You left the freezer open.", "angry");

    private static List<WatchRound> Rounds(int count) => [.. Enumerable.Range(0, count).Select(i => Round(i % 2 == 0 ? WatchRound.Husband : WatchRound.Wife))];

    private static WatchMatch Complete(int roundCount, string winner = "MAH", MatchSide? husband = null, MatchSide? wife = null) => WatchMatch.Complete(
        MatchId.New(),
        husband ?? MatchSide.Persona("MAH", "Matthew"),
        wife ?? MatchSide.Persona("KSH", "Kimberly"),
        "user-1",
        "the freezer",
        Rounds(roundCount),
        winner,
        "The husband held the point.",
        AdvancedStats.Empty,
        AdvancedStats.Empty,
        Ended);

    [Fact]
    public void A_match_carries_six_lines_or_seven_when_a_slap_added_one()
    {
        WatchMatch.MaxRoundsPerMatch.Should().Be(3);
        WatchMatch.MaxLinesPerMatch.Should().Be(7, "a slap inserts one extra reaction, and every slapped match failed at the verdict without the allowance");

        Complete(6).Rounds.Should().HaveCount(6);
        Complete(WatchMatch.MaxLinesPerMatch).Rounds.Should().HaveCount(7);

        var tooMany = () => Complete(WatchMatch.MaxLinesPerMatch + 1);
        tooMany.Should().Throw<InvalidOperationException>().WithMessage("*at most 7*");

        var none = () => WatchMatch.Complete(MatchId.New(), MatchSide.Persona("MAH"), MatchSide.Persona("KSH"), "u", null, [], "MAH", "v", AdvancedStats.Empty, AdvancedStats.Empty, Ended);
        none.Should().Throw<InvalidOperationException>();
    }
}

public class WatchRulesTests
{
    [Fact]
    public void The_human_rules_put_the_players_words_first_and_allow_deflection_only_out_loud()
    {
        WatchRules.HumanReplyLineRules.Should().Contain("ANSWER THEIR LAST LINE", "the player said those words out loud and is waiting to hear them engaged");
        WatchRules.HumanReplyLineRules.Should().Contain("never by ignoring it", "refusing to answer is legitimate; silently changing the subject is the failure mode");
    }

    [Fact]
    public void A_match_is_complete_only_when_both_sides_have_had_all_their_rounds()
    {
        List<WatchRound> Lines(int husband, int wife) =>
        [
            .. Enumerable.Repeat(new WatchRound(WatchRound.Husband, "h", "angry"), husband),
            .. Enumerable.Repeat(new WatchRound(WatchRound.Wife, "w", "angry"), wife),
        ];

        WatchRules.IsComplete(Lines(3, 3)).Should().BeTrue();
        WatchRules.IsComplete(Lines(3, 2)).Should().BeFalse();
        WatchRules.IsComplete(Lines(4, 3)).Should().BeTrue("the slap reaction is an extra line, not an extra round");
        WatchRules.IsComplete([]).Should().BeFalse();
    }
}
