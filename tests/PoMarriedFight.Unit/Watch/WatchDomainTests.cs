using PoMarriedFight.Api.Features.Watch;
using PoMarriedFight.Shared.Identifiers;
using PoMarriedFight.Shared.Models;

namespace PoMarriedFight.Unit.Watch;

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

    [Fact]
    public void The_supplied_id_is_kept_so_the_audio_blobs_line_up()
    {
        var id = MatchId.New();

        var match = WatchMatch.Complete(id, MatchSide.Persona("MAH"), MatchSide.Persona("KSH"), "u", null, Rounds(2), "MAH", "v", AdvancedStats.Empty, AdvancedStats.Empty, Ended);

        match.Id.Should().Be(id);
        match.EndedAt.Should().Be(Ended);
        match.Topic.Should().BeNull();
    }

    [Fact]
    public void The_winner_resolves_to_a_side_case_insensitively_and_a_draw_resolves_to_neither()
    {
        Complete(2, winner: "MAH").WinnerSide()!.Id.Should().Be("MAH");
        Complete(2, winner: "ksh").WinnerSide()!.Id.Should().Be("KSH", "the judge's casing is not the record's problem");
        Complete(2, winner: string.Empty).WinnerSide().Should().BeNull();
        Complete(2, winner: "XXX").WinnerSide().Should().BeNull("a name that is neither side is not a winner");
    }

    [Fact]
    public void A_person_arguing_a_persona_is_recorded_so_their_profile_can_grow_from_the_match()
    {
        var withHuman = Complete(2, wife: MatchSide.Human("KD", "Kim"));

        // A person's record and style profile are built from every debate they speak in.
        withHuman.HumanSides.Select(s => s.Id).Should().Equal("KD");
        withHuman.Husband.IsHuman.Should().BeFalse("the persona side stays an authored profile");
        Complete(2).HumanSides.Should().BeEmpty("two personas arguing involves nobody real");
        Complete(2, husband: MatchSide.Human("MA"), wife: MatchSide.Human("KD")).HumanSides.Should().HaveCount(2);
    }

    [Fact]
    public void Rehydration_restores_everything_the_repository_stored()
    {
        var id = MatchId.New();
        var stats = new AdvancedStats(10, 20, 30, 4, 50, 60, 70, 80, 90, 100);

        var match = WatchMatch.Rehydrate(id, MatchSide.Persona("MAH"), MatchSide.Persona("KSH"), "user-1", "the freezer", "MAH", "held the point", Ended, Rounds(6), stats, AdvancedStats.Empty);

        match.Id.Should().Be(id);
        match.UserId.Should().Be("user-1");
        match.Topic.Should().Be("the freezer");
        match.Winner.Should().Be("MAH");
        match.Verdict.Should().Be("held the point");
        match.HusbandStats.Should().Be(stats);
        match.WifeStats.Should().Be(AdvancedStats.Empty);
        match.Rounds.Should().HaveCount(6);
        match.Rounds[0].AudioFormat.Should().Be("pcm", "an unlabelled round is PCM and must never be relabelled");
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
    public void The_stock_rules_still_drive_an_ordinary_turn_from_personality()
    {
        WatchRules.StockLineRules.Should().Contain("likes/dislikes").And.Contain("slider MUST visibly affect the line",
            "with two full profiles, anchoring on personality is what keeps the characters distinguishable");
        WatchRules.LineRulesFor(opponentIsHuman: false).Should().Be(WatchRules.StockLineRules);
        WatchRules.LineRulesFor(opponentIsHuman: true).Should().Be(WatchRules.HumanReplyLineRules);
    }

    [Theory]
    [InlineData(0, false, WatchRound.Husband)]
    [InlineData(1, false, WatchRound.Wife)]
    [InlineData(2, false, WatchRound.Husband)]
    [InlineData(1, true, WatchRound.Husband)]
    [InlineData(2, true, WatchRound.Wife)]
    public void The_husband_opens_the_two_alternate_and_a_slap_hands_the_turn_back(int linesSoFar, bool afterSlap, string expected) =>
        WatchRules.NextSpeaker(linesSoFar, afterSlap).Should().Be(expected);

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

    [Theory]
    [InlineData("angry", "angry")]
    [InlineData("  FURIOUS  ", "furious")]
    [InlineData("very sarcastic indeed", "sarcastic")]
    [InlineData("elated", "angry")]
    [InlineData(null, "angry")]
    public void An_unknown_mood_lands_on_the_closed_set(string? mood, string expected)
    {
        WatchRules.NormalizeMood(mood).Should().Be(expected);
        WatchRules.Moods.Should().Contain(WatchRules.NormalizeMood(mood));
    }
}

public class InterjectionsTests
{
    [Fact]
    public void Slap_is_the_catalogue_and_an_unknown_key_simply_has_no_directive()
    {
        var slap = Interjections.Find(Interjections.SlapKey);

        slap.Should().NotBeNull();
        slap!.Label.Should().Be("SLAP");
        slap.PromptDirective.Should().Contain("SLAPPED");
        Interjections.Find("EYE-ROLL").Should().BeNull("an old client sending a retired key gets no directive rather than an error");
        Interjections.Find(null).Should().BeNull();
        Interjections.Find(" ").Should().BeNull();
        Interjections.All.Select(i => i.Key).Should().OnlyHaveUniqueItems();
    }
}
