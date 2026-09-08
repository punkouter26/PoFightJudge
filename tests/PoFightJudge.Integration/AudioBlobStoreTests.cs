using System.Text;
using PoFightJudge.Api.Common;
using PoFightJudge.Api.Features.Storage;
using PoFightJudge.Integration.Support;
using PoFightJudge.Shared.Identifiers;

namespace PoFightJudge.Integration;

[Collection(AzuriteCollection.Name)]
public class AudioBlobStoreTests(AzuriteFixture azurite)
{
    private AudioBlobStore Sut()
    {
        Skip.IfNot(azurite.IsAvailable, azurite.Unavailable);
        return new AudioBlobStore(azurite.Blobs, new StorageContainers("test-audio", "test-faces", "test-ttscache"));
    }

    [SkippableFact]
    public async Task Upload_read_and_prefix_delete_round_trip()
    {
        var sut = Sut();
        var match = MatchId.New();
        var players = IAudioBlobStore.PlayersTrack(match);
        var bytes = Encoding.ASCII.GetBytes("RIFF....WAVEfmt fake");

        (await sut.UploadAsync(players, new MemoryStream(bytes), "audio/wav")).Should().Be(players);

        await using (var read = await sut.OpenReadAsync(players))
        {
            read.Should().NotBeNull();
            using var ms = new MemoryStream();
            await read!.CopyToAsync(ms);
            ms.ToArray().Should().Equal(bytes);
        }

        await sut.UploadAsync(IAudioBlobStore.HostTrack(match), new MemoryStream(bytes), "audio/wav");
        await sut.UploadAsync(IAudioBlobStore.Round(match, 0, "mp3"), new MemoryStream(bytes), "audio/mpeg");
        var other = MatchId.New();
        await sut.UploadAsync(IAudioBlobStore.PlayersTrack(other), new MemoryStream(bytes), "audio/wav");

        await sut.DeleteMatchAudioAsync(match);

        (await sut.OpenReadAsync(players)).Should().BeNull();
        (await sut.OpenReadAsync(IAudioBlobStore.HostTrack(match))).Should().BeNull();
        (await sut.OpenReadAsync(IAudioBlobStore.Round(match, 0, "mp3"))).Should().BeNull();
        (await sut.OpenReadAsync(IAudioBlobStore.PlayersTrack(other))).Should().NotBeNull("only the deleted match's prefix is removed");
    }

    [SkippableFact]
    public async Task Missing_blob_reads_as_null_and_the_container_is_created_on_demand()
    {
        var sut = Sut();

        (await sut.OpenReadAsync($"{MatchId.New().Value}/nothing.wav")).Should().BeNull();
    }
}
