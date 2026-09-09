using PoFightJudge.Client.Services;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Unit.Client;

/// <summary>
/// Turning what the browser's recogniser heard into the word-level transcript the analysis reads.
/// </summary>
/// <remarks>
/// The seam has been on the server since T41 — AcceptClientTranscript, ClientTranscript.Sanitize, the endpoint, the
/// wait in the pipeline — and nothing has ever posted to it, because no browser half was ever written. Free is the
/// point: the analysis prefers the host's own live captions, and below MinCaptionTranscriptWords it pays a model to
/// diarize the recording instead. This is a second free source to try before that one.
///
/// The Web Speech API reports no word timings, only whole utterances. So the offsets are interpolated across each
/// utterance between the moment the recogniser started hearing it and the moment it settled — good enough for the
/// speaker mapper, which matches words to turns by overlap, and honest about being an estimate.
/// </remarks>
public class HeardTranscriptTests
{
    private static HeardTranscript Build() => new();

    [Fact]
    public void Words_are_spread_across_the_utterance_they_were_heard_in()
    {
        var sut = Build();

        sut.Heard("you never listen", startSeconds: 10, endSeconds: 13);

        var words = sut.ToTranscript()!.Words;
        words.Select(w => w.Text).Should().Equal("you", "never", "listen");
        words[0].Start.Should().Be(10);
        words[^1].End.Should().Be(13);
        words[1].Start.Should().BeGreaterThan(words[0].Start).And.BeLessThan(words[2].Start);
    }

    [Fact]
    public void Every_word_sits_inside_the_one_before_it_and_the_one_after()
    {
        var sut = Build();
        sut.Heard("that is not what I said", 0, 3);
        sut.Heard("it is exactly what you said", 4, 8);

        var words = sut.ToTranscript()!.Words;

        words.Should().HaveCount(12);
        for (var i = 1; i < words.Count; i++)
        {
            words[i].Start.Should().BeGreaterThanOrEqualTo(words[i - 1].Start);
            words[i].End.Should().BeGreaterThanOrEqualTo(words[i].Start);
        }
    }

    /// <summary>
    /// Both fighters share one microphone, so the browser cannot say who spoke. One label for all of it: the
    /// speaker mapper attributes words by overlapping them with the turn timeline, which is the record of who had
    /// the floor, and a made-up second label would only give it something wrong to believe.
    /// </summary>
    [Fact]
    public void Everything_carries_one_label_because_one_microphone_cannot_tell_two_people_apart()
    {
        var sut = Build();
        sut.Heard("you never listen", 1, 2);
        sut.Heard("I always listen", 3, 4);

        sut.ToTranscript()!.Words.Select(w => w.Label).Distinct().Should().ContainSingle();
    }

    [Fact]
    public void The_text_reads_as_the_words_in_order()
    {
        var sut = Build();
        sut.Heard("you never listen", 1, 2);
        sut.Heard("rubbish", 3, 4);

        sut.ToTranscript()!.Text.Should().Be("you never listen rubbish");
    }

    [Fact]
    public void Nothing_heard_is_no_transcript_rather_than_an_empty_one()
    {
        Build().ToTranscript().Should().BeNull();
        var blank = Build();
        blank.Heard("   ", 1, 2);
        blank.ToTranscript().Should().BeNull("whitespace is not something somebody said");
    }

    /// <summary>An utterance the recogniser timed backwards, or in no time at all, still has to produce sane offsets.</summary>
    [Fact]
    public void An_utterance_with_no_duration_still_produces_ordered_words()
    {
        var sut = Build();

        sut.Heard("say that again", startSeconds: 5, endSeconds: 5);

        var words = sut.ToTranscript()!.Words;
        words.Should().HaveCount(3);
        words.Should().OnlyContain(w => w.Start >= 5 && w.End >= w.Start);
    }

    [Fact]
    public void An_utterance_timed_backwards_is_read_the_right_way_round()
    {
        var sut = Build();

        sut.Heard("you did", startSeconds: 9, endSeconds: 4);

        var words = sut.ToTranscript()!.Words;
        words[0].Start.Should().BeLessThanOrEqualTo(words[^1].End);
    }

    /// <summary>
    /// A fight runs for minutes and the recogniser fires constantly. The cap is the same one the server enforces on
    /// the way in, applied here so the browser never builds a body the endpoint is only going to refuse.
    /// </summary>
    [Fact]
    public void It_stops_collecting_at_the_bound_the_server_would_refuse_past()
    {
        var sut = Build();

        for (var i = 0; i < 3000; i++)
        {
            sut.Heard("one two three four five six seven eight nine ten", i, i + 1);
        }

        sut.ToTranscript()!.Words.Should().HaveCountLessThanOrEqualTo(HeardTranscript.MaxWords);
    }

    /// <summary>What the server will do with it, so the two halves are known to agree.</summary>
    [Fact]
    public void What_it_produces_survives_the_server_side_bounding()
    {
        var sut = Build();
        sut.Heard("you never listen to a word of it", 1, 4);

        var sanitized = PoFightJudge.Api.Features.Analysis.ClientTranscript.Sanitize(sut.ToTranscript());

        sanitized.Should().NotBeNull();
        sanitized!.Words.Should().HaveCount(8, "nothing it produces is something the endpoint throws away");
    }
}
