using PoFightJudge.Api.Features.Analysis;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Unit.Analysis;

/// <summary>
/// Turning a diarizer's anonymous labels into the two people in the room. It works by overlap with the turn
/// timeline, because the host knows who it handed the floor to and a diarizer only knows that voices differ.
/// </summary>
/// <summary>
/// Turning a diarizer's anonymous labels into the two people in the room. It works by overlap with the turn
/// timeline, because the host knows who it handed the floor to and a diarizer only knows that voices differ.
/// </summary>
public class SpeakerMapperTests
{
    private static readonly MatchId Match = MatchId.New();

    private static TurnDto Turn(Speaker speaker, double start, double end, TurnKind kind = TurnKind.Talk) =>
        new(Match, 0, speaker, kind, string.Empty) { StartSeconds = start, EndSeconds = end };

    private static TranscriptDto Heard(params (string Text, string Label, double Start, double End)[] words) =>
        new(string.Join(' ', words.Select(w => w.Text)), [.. words.Select(w => new TranscriptWord(w.Text, w.Label, w.Start, w.End))]);

    [Fact]
    public void Labels_are_matched_to_whoever_had_the_floor_at_the_time()
    {
        var transcript = Heard(
            ("you", "spk_1", 1, 1.4),
            ("never", "spk_1", 1.4, 2),
            ("rubbish", "spk_2", 11, 11.6));

        var mapped = SpeakerMapper.Map(transcript, [Turn(Speaker.Player1, 0, 5), Turn(Speaker.Player2, 10, 15)]);

        mapped.For(Speaker.Player1).Select(w => w.Text).Should().Equal(["you", "never"]);
        mapped.For(Speaker.Player2).Select(w => w.Text).Should().Equal(["rubbish"]);
        mapped.Flagged.Should().BeFalse();
    }
}

