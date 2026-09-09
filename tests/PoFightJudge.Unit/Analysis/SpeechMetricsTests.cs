using PoFightJudge.Api.Features.Analysis;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Unit.Analysis;

/// <summary>
/// What can be counted rather than judged. These numbers are the half of a report that survives a failed judge
/// call, so they are computed from the transcript and the turn timeline and nothing else.
/// </summary>
/// <summary>
/// What can be counted rather than judged. These numbers are the half of a report that survives a failed judge
/// call, so they are computed from the transcript and the turn timeline and nothing else.
/// </summary>
public class SpeechMetricsTests
{
    private static readonly MatchId Match = MatchId.New();

    private static MappedTranscript Transcript(params (string Text, Speaker Speaker, double Start, double End)[] words) =>
        new([.. words.Select(w => new MappedWord(w.Text, w.Speaker, w.Start, w.End))], false, string.Empty);

    /// <summary>Lays a sentence down as one word per fifth of a second from <paramref name="start"/>.</summary>
    private static MappedWord[] Said(string sentence, Speaker speaker, double start)
    {
        var words = sentence.Split(' ');
        return [.. words.Select((w, i) => new MappedWord(w, speaker, start + (i * 0.2), start + (i * 0.2) + 0.2))];
    }

    private static TurnDto Turn(Speaker speaker, double start, double end, TurnKind kind = TurnKind.Talk) =>
        new(Match, 0, speaker, kind, string.Empty) { StartSeconds = start, EndSeconds = end };

    [Fact]
    public void Words_and_pace_are_measured_from_what_that_fighter_actually_said()
    {
        var words = Said("you never listen to a word I say", Speaker.Player1, 10)
            .Concat(Said("rubbish", Speaker.Player2, 20))
            .ToArray();
        var transcript = new MappedTranscript(words, false, string.Empty);

        var metrics = SpeechMetrics.Compute(Speaker.Player1, transcript, [Turn(Speaker.Player1, 10, 12)]);

        metrics.Words.Should().Be(8, "only their own words count");
        metrics.WordsPerMinute.Should().BeGreaterThan(0);
        metrics.LongestWord.Should().Be("listen");
        metrics.SecondPersonRatio.Should().BeGreaterThan(0, "\"you\" is the whole shape of this argument");
    }
}
