using PoFightJudge.Shared.Models;

namespace PoFightJudge.Unit.Shared;

/// <summary>
/// A generated line ends with a stage cue — "(smug)", "(seething)" — that the voice acts out. On screen it is
/// noise: the mood chip already says it, so the transcript shows the words alone.
/// </summary>
public class StageCueTests
{
    [Theory]
    [InlineData("Put down the phone. (smug)", "Put down the phone.")]
    [InlineData("That is inconvenient.(frustrated)", "That is inconvenient.")]
    [InlineData("(coldly) You again.", "You again.")]
    [InlineData("Fine (snaps) — whatever you say.", "Fine — whatever you say.")]
    [InlineData("No cue here.", "No cue here.")]
    public void Shown_drops_the_cue_and_tidies_the_gap(string line, string shown) =>
        WatchTurns.Shown(line).Should().Be(shown);

    [Fact]
    public void A_line_that_is_only_a_cue_is_left_alone_rather_than_blanked() =>
        WatchTurns.Shown("(sighs)").Should().Be("(sighs)");
}
