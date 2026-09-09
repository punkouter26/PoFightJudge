using PoFightJudge.Client.Pages;

namespace PoFightJudge.Unit.Watch;

/// <summary>
/// The two decisions behind a hands-free turn: when a pause has run long enough to stop recording, and whether what
/// came back is worth sending. The meter and the microphone are the browser's business; these are not.
/// </summary>
public class SpokenTurnTests
{
    [Fact]
    public void A_two_second_pause_ends_the_turn()
    {
        WatchPlay.PauseEndsTurn(heardSomething: true, quietFor: TimeSpan.FromSeconds(2))
            .Should().BeTrue("two seconds of quiet after somebody has spoken is the whole stop gesture");
    }

    [Fact]
    public void A_shorter_pause_is_still_a_pause()
    {
        WatchPlay.PauseEndsTurn(heardSomething: true, quietFor: TimeSpan.FromSeconds(1.9))
            .Should().BeFalse("thinking mid-sentence must not send the turn out from under them");
    }

    [Fact]
    public void Silence_before_a_word_is_spoken_never_ends_the_turn()
    {
        WatchPlay.PauseEndsTurn(heardSomething: false, quietFor: TimeSpan.FromMinutes(5))
            .Should().BeFalse("the microphone opens with the turn, so somebody still gathering themselves would be cut off at once");
    }

    [Theory]
    [InlineData("That is not what I said.")]
    [InlineData("  and you know it  ")]
    public void A_transcript_with_words_in_it_is_sent(string heard)
    {
        WatchPlay.WorthSending(heard).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_blank_transcript_is_not_sent(string? heard)
    {
        WatchPlay.WorthSending(heard).Should().BeFalse("a clip with no words in it would otherwise put an empty turn on their record");
    }
}
