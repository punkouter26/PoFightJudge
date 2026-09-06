using PoMarriedFight.Api.Features.Voice;

namespace PoMarriedFight.Unit.Voice;

public class SentenceChunkerTests
{
    private const string ThreeSentences =
        "You left the freezer open all night and every single thing in it is ruined. "
        + "That is the third time this month, and I am the one who noticed it. "
        + "Do not stand there and tell me I am overreacting about this. (seething)";

    private static string Normalize(string s) => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    [Fact]
    public void Short_lines_empty_input_and_decimals_are_not_split()
    {
        SentenceChunker.Split("Fine. Whatever. (coldly)").Should().ContainSingle("the round trip per chunk costs more than the head start it buys");
        SentenceChunker.Split("   ").Should().BeEmpty();
        SentenceChunker.Split(null).Should().BeEmpty();
        var decimals = "Mrs. Patel said the bill was 3.5 percent higher than last month and honestly that tracks with everything else in this house lately. So no.";
        SentenceChunker.Split(decimals).Should().HaveCount(1, "a period without whitespace after it is not a sentence boundary and the tail runt merges back");
    }

    [Fact]
    public void Every_word_survives_the_split_in_order_and_a_trailing_stage_cue_never_stands_alone()
    {
        var chunks = SentenceChunker.Split(ThreeSentences);

        chunks.Count.Should().BeGreaterThan(1);
        Normalize(string.Join(' ', chunks)).Should().Be(Normalize(ThreeSentences));
        var last = chunks[^1];
        last.Should().NotBe("(seething)").And.EndWith("(seething)");
        chunks.Should().OnlyContain(c => c.Length >= SentenceChunker.MinChunkChars || string.Equals(c, last, StringComparison.Ordinal));
    }

    [Fact]
    public void Runs_of_punctuation_are_one_boundary_not_three()
    {
        var text = "Are you seriously asking me that question right now, after everything?! "
            + "Because from where I am standing that is not a question at all, my love...";

        var chunks = SentenceChunker.Split(text);

        chunks.Should().HaveCount(2, "each sentence is long enough to be worth its own call");
        chunks[0].Should().EndWith("everything?!");
        chunks[1].Should().StartWith("Because").And.EndWith("my love...");
        chunks.Should().OnlyContain(c => !char.IsPunctuation(c[0]), "a run of ?! or ... must not open the next chunk");
    }
}
