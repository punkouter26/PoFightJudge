using PoFightJudge.Api.Features.Fight;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Unit.Fight;

/// <summary>
/// The transcript built out of the host's own live captions. It stands in for a separate diarization call: the
/// orchestrator knows who holds the floor, so the label comes from the turn state rather than a diarizer's guess.
/// The trade is precision, and these tests pin what that trade actually is.
/// </summary>
/// <summary>
/// The transcript built out of the host's own live captions. It stands in for a separate diarization call: the
/// orchestrator knows who holds the floor, so the label comes from the turn state rather than a diarizer's guess.
/// The trade is precision, and these tests pin what that trade actually is.
/// </summary>
public class CaptionTranscriptTests
{

    [Fact]
    public void Offsets_run_forwards_never_overlap_and_allow_for_the_lag_before_a_caption_arrives()
    {
        var captions = new CaptionTranscript();
        captions.Add("first chunk here", Speaker.Player1, 10);
        captions.Add("second chunk here", Speaker.Player1, 14);

        var words = captions.Build()!.Words;

        words.Should().HaveCount(6);
        words[0].Start.Should().BeLessThan(words[0].End);
        words.Should().BeInAscendingOrder(w => w.Start);
        for (var i = 1; i < words.Count; i++)
        {
            words[i].Start.Should().BeGreaterThanOrEqualTo(words[i - 1].End - 0.001, "one word cannot start before the last one finished");
        }

        words[^1].End.Should().BeApproximately(14 - CaptionTranscript.LagSeconds, 0.05, "captions arrive after the words were spoken");
    }
}
