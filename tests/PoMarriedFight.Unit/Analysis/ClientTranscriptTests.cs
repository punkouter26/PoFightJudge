using PoMarriedFight.Api.Features.Analysis;
using PoMarriedFight.Shared.Models;

namespace PoMarriedFight.Unit.Analysis;

/// <summary>
/// Bounding a transcript a browser posted. It ends up in the judge's prompt as data, so what matters here is size
/// and shape rather than meaning.
/// </summary>
public class ClientTranscriptTests
{
    private static TranscriptDto Posted(params TranscriptWord[] words) => new("whatever they claimed", words);

    [Fact]
    public void Nothing_usable_comes_back_as_nothing()
    {
        ClientTranscript.Sanitize(null).Should().BeNull();
        ClientTranscript.Sanitize(Posted()).Should().BeNull();
        ClientTranscript.Sanitize(Posted(new TranscriptWord("   ", "spk_1", 0, 1))).Should().BeNull();
    }

    [Fact]
    public void A_transcript_longer_than_any_recording_could_be_is_refused_outright()
    {
        var enormous = Posted([.. Enumerable.Range(0, ClientTranscript.MaxWords + 1).Select(i => new TranscriptWord($"w{i}", "spk_1", i, i + 1))]);

        ClientTranscript.Sanitize(enormous).Should().BeNull();
    }

    [Fact]
    public void Long_words_and_labels_are_cut_to_size()
    {
        var clean = ClientTranscript.Sanitize(Posted(
            new TranscriptWord(new string('x', 500), new string('l', 100), 1, 2)));

        clean!.Words[0].Text.Should().HaveLength(ClientTranscript.MaxWordLength);
        clean.Words[0].Label.Should().HaveLength(ClientTranscript.MaxLabelLength);
    }

    [Fact]
    public void Impossible_offsets_are_brought_back_into_the_recording()
    {
        var clean = ClientTranscript.Sanitize(Posted(
            new TranscriptWord("a", "spk_1", double.NaN, double.PositiveInfinity),
            new TranscriptWord("b", "spk_1", -50, 999_999),
            new TranscriptWord("c", "spk_1", 10, 2)));

        clean!.Words.Should().OnlyContain(w => w.Start >= 0 && w.End <= ClientTranscript.MaxSeconds);
        clean.Words.Should().OnlyContain(w => w.End >= w.Start, "a word cannot finish before it started");
    }

    [Fact]
    public void A_word_with_no_label_is_given_one_rather_than_dropped()
    {
        var clean = ClientTranscript.Sanitize(Posted(new TranscriptWord("hello", "  ", 0, 1)));

        clean!.Words[0].Label.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void The_text_is_rebuilt_from_the_words_rather_than_believed()
    {
        var clean = ClientTranscript.Sanitize(Posted(
            new TranscriptWord("you", "spk_1", 0, 1),
            new TranscriptWord("never", "spk_1", 1, 2)));

        clean!.Text.Should().Be("you never", "nothing downstream reads the posted text, and it would otherwise be unbounded");
    }
}
