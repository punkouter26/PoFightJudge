using PoFightJudge.Api.Features.Analysis;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Unit.Analysis;

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

    [Fact]
    public void Whatever_the_diarizer_calls_them_is_none_of_our_business()
    {
        var transcript = Heard(("you", "speaker_A", 1, 2), ("rubbish", "speaker_B", 11, 12));

        var mapped = SpeakerMapper.Map(transcript, [Turn(Speaker.Player1, 0, 5), Turn(Speaker.Player2, 10, 15)]);

        mapped.For(Speaker.Player1).Should().ContainSingle();
        mapped.For(Speaker.Player2).Should().ContainSingle();
    }

    [Fact]
    public void A_transcript_with_no_words_in_it_says_so_rather_than_pretending()
    {
        var mapped = SpeakerMapper.Map(new TranscriptDto(string.Empty, []), [Turn(Speaker.Player1, 0, 5)]);

        mapped.Words.Should().BeEmpty();
        mapped.Flagged.Should().BeTrue();
        mapped.Note.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void A_third_voice_is_dropped_rather_than_put_in_somebodys_mouth()
    {
        var transcript = Heard(
            ("you", "spk_1", 1, 2),
            ("rubbish", "spk_2", 11, 12),
            ("and", "spk_3", 30, 31),
            ("time", "spk_3", 31, 32));

        var mapped = SpeakerMapper.Map(transcript, [Turn(Speaker.Player1, 0, 5), Turn(Speaker.Player2, 10, 15)]);

        mapped.Words.Should().HaveCount(2, "the host is in the recording too, and its words are not theirs");
        mapped.Words.Should().NotContain(w => w.Text == "time");
    }

    [Fact]
    public void A_mapping_the_timeline_cannot_support_is_flagged_rather_than_guessed_quietly()
    {
        // Every word lands while the same person had the floor, so there is nothing to tell the two labels apart.
        var transcript = Heard(("you", "spk_1", 1, 2), ("never", "spk_2", 2, 3));

        var mapped = SpeakerMapper.Map(transcript, [Turn(Speaker.Player1, 0, 5)]);

        mapped.Flagged.Should().BeTrue();
        mapped.Note.Should().NotBeNullOrWhiteSpace("the report has to be able to say the attribution is shaky");
    }

    [Fact]
    public void With_no_turns_at_all_nothing_can_be_attributed()
    {
        var transcript = Heard(("you", "spk_1", 1, 2), ("rubbish", "spk_2", 11, 12));

        var mapped = SpeakerMapper.Map(transcript, []);

        mapped.Flagged.Should().BeTrue();
    }
}

/// <summary>
/// Finding the moment behind a quote. The judge says what was said; this locates when, so it can be played back.
/// </summary>
public class HighlightFinderTests
{
    private static MappedTranscript Transcript() => new(
        [
            new MappedWord("you", Speaker.Player1, 1, 1.3),
            new MappedWord("never", Speaker.Player1, 1.3, 1.7),
            new MappedWord("listen", Speaker.Player1, 1.7, 2.2),
            new MappedWord("to", Speaker.Player1, 2.2, 2.4),
            new MappedWord("me", Speaker.Player1, 2.4, 2.8),
            new MappedWord("that", Speaker.Player2, 5, 5.3),
            new MappedWord("is", Speaker.Player2, 5.3, 5.5),
            new MappedWord("nonsense", Speaker.Player2, 5.5, 6.2),
        ],
        false,
        string.Empty);

    [Fact]
    public void A_quote_is_located_where_it_was_actually_said()
    {
        var found = HighlightFinder.Locate(Speaker.Player1, "Best moment", "never listen to me", Transcript());

        found.Should().NotBeNull();

        // Padded on both sides on purpose: a clip that starts exactly on the first syllable sounds clipped.
        found!.StartSeconds.Should().BeLessThanOrEqualTo(1.3).And.BeGreaterThanOrEqualTo(0);
        found.EndSeconds.Should().BeGreaterThanOrEqualTo(2.8);
        found.Label.Should().Be("Best moment");
        found.Speaker.Should().Be(Speaker.Player1);
    }

    [Fact]
    public void Punctuation_and_case_do_not_stop_a_quote_being_found()
    {
        HighlightFinder.Locate(Speaker.Player1, "Best", "\"Never listen, to me!\"", Transcript()).Should().NotBeNull();
    }

    [Fact]
    public void A_quote_attributed_to_the_wrong_person_is_not_found_in_their_words()
    {
        HighlightFinder.Locate(Speaker.Player2, "Best", "never listen to me", Transcript())
            .Should().BeNull("their opponent said that, and a clip of the wrong voice is worse than no clip");
    }

    [Fact]
    public void Nothing_to_look_for_is_not_a_failure()
    {
        HighlightFinder.Locate(Speaker.Player1, "Best", null, Transcript()).Should().BeNull();
        HighlightFinder.Locate(Speaker.Player1, "Best", "   ", Transcript()).Should().BeNull();
        HighlightFinder.Locate(Speaker.Player1, "Best", "words never spoken here", Transcript()).Should().BeNull();
    }
}
