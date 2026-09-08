using Microsoft.Extensions.Logging.Abstractions;
using PoFightJudge.Api.Common;
using PoFightJudge.Api.Features.Voice;
using PoFightJudge.Integration.Support;

namespace PoFightJudge.Integration;

[Collection(AzuriteCollection.Name)]
public class BlobTtsCacheTests(AzuriteFixture azurite)
{
    private BlobTtsCache Sut()
    {
        Skip.IfNot(azurite.IsAvailable, azurite.Unavailable);
        return new BlobTtsCache(azurite.Blobs, new StorageContainers("test-audio", "test-faces", "test-ttscache"), NullLogger<BlobTtsCache>.Instance);
    }

    [SkippableFact]
    public async Task An_entry_round_trips_with_its_format_and_a_miss_is_null()
    {
        var sut = Sut();
        var key = $"gemini|mp3|Kore|1.00|1.00||{Guid.NewGuid()}";

        (await sut.TryGetAsync(key)).Should().BeNull("nothing cached yet");

        var mp3 = new TtsAudio(Convert.ToBase64String([1, 2, 3, 4]), TtsAudioFormats.Mp3);
        await sut.SetAsync(key, mp3);

        var hit = await sut.TryGetAsync(key);
        hit.Should().NotBeNull();
        hit!.Value.Should().Be(mp3, "the bytes and the format both survive");

        var pcmKey = $"gemini|pcm|Kore|1.00|1.00||{Guid.NewGuid()}";
        var pcm = new TtsAudio(Convert.ToBase64String([9, 9]), TtsAudioFormats.Pcm);
        await sut.SetAsync(pcmKey, pcm);
        (await sut.TryGetAsync(pcmKey))!.Value.Should().Be(pcm);
    }

    [SkippableFact]
    public async Task Empty_audio_is_never_stored_and_writing_twice_is_harmless()
    {
        var sut = Sut();
        var key = $"gemini|mp3|Kore|1.00|1.00||{Guid.NewGuid()}";

        await sut.SetAsync(key, TtsAudio.None);
        (await sut.TryGetAsync(key)).Should().BeNull("silence is not worth caching");

        var audio = new TtsAudio(Convert.ToBase64String([7]), TtsAudioFormats.Mp3);
        await sut.SetAsync(key, audio);
        var act = () => sut.SetAsync(key, audio);

        await act.Should().NotThrowAsync("a re-synthesis of the same line must not fail the round");
        (await sut.TryGetAsync(key))!.Value.Base64.Should().Be(audio.Base64);
        sut.IsEnabled.Should().BeTrue();
    }
}
