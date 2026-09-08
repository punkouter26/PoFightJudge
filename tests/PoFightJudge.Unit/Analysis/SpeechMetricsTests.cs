using PoFightJudge.Api.Features.Analysis;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Unit.Analysis;

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
    public void A_fighter_who_said_nothing_measures_as_nothing_rather_than_dividing_by_zero()
    {
        var metrics = SpeechMetrics.Compute(Speaker.Player1, Transcript(), []);

        metrics.Words.Should().Be(0);
        metrics.WordsPerMinute.Should().Be(0);
        metrics.TalkShare.Should().Be(0);
        metrics.TypeTokenRatio.Should().Be(0);
        metrics.LongestWord.Should().BeEmpty();
    }

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

    [Fact]
    public void Talk_share_is_the_split_between_the_two_of_them()
    {
        var words = Said("one two three four", Speaker.Player1, 0)
            .Concat(Said("five six", Speaker.Player2, 10))
            .ToArray();
        var transcript = new MappedTranscript(words, false, string.Empty);

        var one = SpeechMetrics.Compute(Speaker.Player1, transcript, []);
        var two = SpeechMetrics.Compute(Speaker.Player2, transcript, []);

        one.TalkShare.Should().BeGreaterThan(two.TalkShare);
        (one.TalkShare + two.TalkShare).Should().BeApproximately(1.0, 0.01, "between them they said everything that was said");
    }

    [Fact]
    public void Fillers_hedges_and_absolutes_are_counted_because_they_are_what_a_habit_sounds_like()
    {
        var transcript = new MappedTranscript(
            Said("um I think you always do this basically", Speaker.Player1, 0),
            false,
            string.Empty);

        var metrics = SpeechMetrics.Compute(Speaker.Player1, transcript, []);

        metrics.FillersPer100.Should().BeGreaterThan(0);
        metrics.Hedges.Should().BeGreaterThan(0, "\"I think\" is a hedge");
        metrics.Absolutes.Should().BeGreaterThan(0, "\"always\" is an absolute");
    }

    [Fact]
    public void A_question_counts_whether_or_not_the_transcript_kept_the_question_mark()
    {
        var marked = new MappedTranscript(Said("is that really what you meant?", Speaker.Player1, 0), false, string.Empty);
        var unmarked = new MappedTranscript(Said("why did you do that", Speaker.Player1, 0), false, string.Empty);

        SpeechMetrics.Compute(Speaker.Player1, marked, []).Questions.Should().BeGreaterThan(0);
        SpeechMetrics.Compute(Speaker.Player1, unmarked, []).Questions
            .Should().BeGreaterThan(0, "a transcriber that drops the mark has not stopped it being a question");
    }

    [Fact]
    public void Turns_pauses_and_the_longest_stretch_come_from_the_timeline()
    {
        var words = Said("first", Speaker.Player1, 0)
            .Concat(Said("second after a beat", Speaker.Player1, 6))
            .ToArray();
        var transcript = new MappedTranscript(words, false, string.Empty);

        var metrics = SpeechMetrics.Compute(Speaker.Player1, transcript, [Turn(Speaker.Player1, 0, 5), Turn(Speaker.Player1, 30, 45)]);

        metrics.Turns.Should().Be(2);
        metrics.LongestTurnSeconds.Should().Be(15);
        metrics.Pauses.Should().Be(1, "a few seconds of nothing mid-sentence is a pause");
        metrics.LongestPauseSeconds.Should().BeGreaterThan(5);
    }

    [Fact]
    public void A_long_silence_is_the_other_one_talking_rather_than_a_pause()
    {
        var words = Said("first", Speaker.Player1, 0)
            .Concat(Said("much later", Speaker.Player1, 40))
            .ToArray();
        var transcript = new MappedTranscript(words, false, string.Empty);

        var metrics = SpeechMetrics.Compute(Speaker.Player1, transcript, []);

        metrics.Pauses.Should().Be(0, "somebody who stopped for half a minute had the floor taken off them");
    }

    [Fact]
    public void Being_cut_off_by_the_host_is_counted_separately_from_cutting_off_the_other_one()
    {
        var words = Said("as I was saying", Speaker.Player1, 0).Concat(Said("no you were not", Speaker.Player2, 0.5)).ToArray();
        var transcript = new MappedTranscript(words, false, string.Empty);

        var metrics = SpeechMetrics.Compute(
            Speaker.Player1,
            transcript,
            [Turn(Speaker.Player1, 0, 5), Turn(Speaker.Player1, 2, 2, TurnKind.Interrupt)]);

        metrics.HostInterrupts.Should().Be(1);
        metrics.InterruptionsReceived.Should().BeGreaterThan(0, "the other one started talking while they still were");
    }

    [Fact]
    public void Readability_is_reported_on_both_scales_because_they_disagree_usefully()
    {
        var simple = new MappedTranscript(Said("I like it. It is good. We can go.", Speaker.Player1, 0), false, string.Empty);
        var dense = new MappedTranscript(
            Said("The fundamental incompatibility demonstrates considerable methodological inconsistency.", Speaker.Player1, 0),
            false,
            string.Empty);

        var easy = SpeechMetrics.Compute(Speaker.Player1, simple, []);
        var hard = SpeechMetrics.Compute(Speaker.Player1, dense, []);

        easy.FleschReadingEase.Should().BeGreaterThan(hard.FleschReadingEase);
        hard.FleschKincaidGrade.Should().BeGreaterThan(easy.FleschKincaidGrade);
        hard.SyllablesPerWord.Should().BeGreaterThan(easy.SyllablesPerWord);
    }

    [Fact]
    public void A_phrase_somebody_keeps_saying_is_picked_out_and_common_words_are_not()
    {
        var transcript = new MappedTranscript(
            Said("that is not the point that is not the point at all", Speaker.Player1, 0),
            false,
            string.Empty);

        var metrics = SpeechMetrics.Compute(Speaker.Player1, transcript, []);

        metrics.RepeatedPhrases.Should().NotBeEmpty();
        metrics.TopWords.Should().NotContain("the", "a list of everybody's most common word is the same list every time");
    }
}
