using PoMarriedFight.Api.Features.Fight;
using PoMarriedFight.Shared.Models;

namespace PoMarriedFight.Unit.Fight;

/// <summary>
/// The transcript built out of the host's own live captions. It stands in for a separate diarization call: the
/// orchestrator knows who holds the floor, so the label comes from the turn state rather than a diarizer's guess.
/// The trade is precision, and these tests pin what that trade actually is.
/// </summary>
public class CaptionTranscriptTests
{
    [Fact]
    public void Nothing_heard_is_no_transcript_rather_than_an_empty_one()
    {
        var captions = new CaptionTranscript();

        captions.Build().Should().BeNull();

        captions.Add("   ", Speaker.Player1, 5);
        captions.Add(null, Speaker.Player1, 6);
        captions.Count.Should().Be(0, "blank captions are not speech");
        captions.Build().Should().BeNull();
    }

    [Fact]
    public void Words_carry_the_label_of_whoever_had_the_floor_when_they_arrived()
    {
        var captions = new CaptionTranscript();
        captions.Add("you never listen", Speaker.Player1, 10);
        captions.Add("I listen plenty", Speaker.Player2, 14);
        captions.Add("who said that", null, 18);

        var transcript = captions.Build();

        transcript.Should().NotBeNull();
        transcript!.Words.Where(w => string.Equals(w.Label, "live_1", StringComparison.Ordinal)).Select(w => w.Text).Should().Equal(["you", "never", "listen"]);
        transcript.Words.Where(w => string.Equals(w.Label, "live_2", StringComparison.Ordinal)).Select(w => w.Text).Should().Equal(["I", "listen", "plenty"]);
        transcript.Words.Where(w => string.Equals(w.Label, CaptionTranscript.UnknownLabel, StringComparison.Ordinal)).Select(w => w.Text)
            .Should().Equal(["who", "said", "that"], "speech while nobody holds the floor is still speech");
        transcript.Text.Should().Be("you never listen I listen plenty who said that");
    }

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

    [Fact]
    public void A_chunk_after_a_long_silence_covers_a_bounded_stretch_rather_than_the_whole_gap()
    {
        var captions = new CaptionTranscript();
        captions.Add("much later", Speaker.Player1, 600);

        var words = captions.Build()!.Words;

        (words[^1].End - words[0].Start).Should().BeLessThanOrEqualTo(CaptionTranscript.MaxChunkSeconds + 0.01,
            "a caption after ten minutes of quiet did not take ten minutes to say");
        words[0].Start.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Every_word_has_a_span_so_the_speaker_mapper_can_overlap_it()
    {
        var captions = new CaptionTranscript();
        captions.Add(string.Join(' ', Enumerable.Repeat("word", 200)), Speaker.Player2, 1);

        var words = captions.Build()!.Words;

        words.Should().OnlyContain(w => w.End - w.Start >= CaptionTranscript.MinWordSeconds - 0.001);
    }

    [Fact]
    public void A_runaway_session_cannot_grow_the_transcript_without_limit()
    {
        var captions = new CaptionTranscript();
        for (var i = 0; i < CaptionTranscript.MaxChunks + 100; i++)
        {
            captions.Add("word", Speaker.Player1, i);
        }

        captions.Count.Should().Be(CaptionTranscript.MaxChunks);
        captions.Build()!.Words.Count.Should().BeLessThanOrEqualTo(CaptionTranscript.MaxWords);
    }

    [Fact]
    public void A_caption_stamped_with_nonsense_is_dropped_rather_than_poisoning_the_offsets()
    {
        var captions = new CaptionTranscript();
        captions.Add("bad", Speaker.Player1, double.NaN);
        captions.Add("worse", Speaker.Player1, double.PositiveInfinity);

        captions.Count.Should().Be(0);
    }

    [Fact]
    public void Elapsed_is_measured_from_the_start_of_the_show_and_never_goes_negative()
    {
        var start = new DateTimeOffset(2026, 9, 6, 19, 0, 0, TimeSpan.Zero);

        CaptionTranscript.Elapsed(start.AddSeconds(12.3456), start).Should().Be(12.35, "offsets are kept to hundredths");
        CaptionTranscript.Elapsed(start.AddSeconds(-5), start).Should().Be(0, "a clock that ran backwards is not a negative offset");
    }
}
