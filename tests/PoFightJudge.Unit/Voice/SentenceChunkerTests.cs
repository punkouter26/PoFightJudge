using PoFightJudge.Api.Features.Voice;

namespace PoFightJudge.Unit.Voice;

public class SentenceChunkerTests
{
    private const string ThreeSentences =
        "You left the freezer open all night and every single thing in it is ruined. "
        + "That is the third time this month, and I am the one who noticed it. "
        + "Do not stand there and tell me I am overreacting about this. (seething)";

    private static string Normalize(string s) => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

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
}
