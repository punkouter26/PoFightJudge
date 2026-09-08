using PoFightJudge.Api.Features.Voice;

namespace PoFightJudge.Unit.Voice;

public class TtsCacheKeyTests
{
    [Fact]
    public void A_key_becomes_a_sharded_hashed_blob_name_with_the_format_extension()
    {
        var name = TtsCacheKey.BlobName("gemini|mp3|Kore|1.00|1.00||Fine.", TtsAudioFormats.Mp3);

        name.Should().MatchRegex("^[0-9a-f]{2}/[0-9a-f]{64}\\.mp3$");
        name[..2].Should().Be(name[3..5], "the shard is the first two characters of the hash");
        TtsCacheKey.BlobName("gemini|pcm|Kore|1.00|1.00||Fine.", TtsAudioFormats.Pcm).Should().EndWith(".pcm");
    }

    [Fact]
    public void The_same_key_is_stable_and_any_difference_lands_somewhere_else()
    {
        const string key = "gemini|mp3|Kore|1.00|1.00||Fine.";

        TtsCacheKey.BlobName(key, TtsAudioFormats.Mp3).Should().Be(TtsCacheKey.BlobName(key, TtsAudioFormats.Mp3));
        TtsCacheKey.BlobName("gemini|mp3|Charon|1.00|1.00||Fine.", TtsAudioFormats.Mp3).Should().NotBe(TtsCacheKey.BlobName(key, TtsAudioFormats.Mp3));
        TtsCacheKey.BlobName(key, TtsAudioFormats.Pcm).Should().NotBe(TtsCacheKey.BlobName(key, TtsAudioFormats.Mp3), "the extension separates the formats");
        TtsCacheKey.BlobName("dialogue with / slashes, quotes \" and ünïcode", TtsAudioFormats.Mp3).Should().MatchRegex("^[0-9a-f]{2}/[0-9a-f]{64}\\.mp3$", "hashing keeps the name legal whatever was said");
    }
}
