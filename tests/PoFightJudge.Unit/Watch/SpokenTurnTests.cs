using PoFightJudge.Client.Pages;

namespace PoFightJudge.Unit.Watch;

/// <summary>
/// The two decisions behind a hands-free turn: when a pause has run long enough to stop recording,
/// and whether what came back is worth sending.
/// </summary>
public class SpokenTurnTests
{
    [Fact]
    public void Pause_detection_rules()
    {
        // Two seconds of quiet after speaking ends the turn
        WatchPlay.PauseEndsTurn(heardSomething: true, quietFor: TimeSpan.FromSeconds(2))
            .Should().BeTrue("two seconds of quiet after somebody has spoken is the whole stop gesture");

        // A shorter pause is still a pause
        WatchPlay.PauseEndsTurn(heardSomething: true, quietFor: TimeSpan.FromSeconds(1.9))
            .Should().BeFalse("thinking mid-sentence must not send the turn out from under them");

        // Silence before a word is spoken never ends the turn
        WatchPlay.PauseEndsTurn(heardSomething: false, quietFor: TimeSpan.FromMinutes(5))
            .Should().BeFalse("the microphone opens with the turn, so somebody still gathering themselves would be cut off at once");
    }

    [Theory]
    [InlineData("That is not what I said.", true)]
    [InlineData("  and you know it  ", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void Transcript_validation_rules(string? heard, bool expected)
    {
        WatchPlay.WorthSending(heard).Should().Be(expected);
    }
}
