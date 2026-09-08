using PoFightJudge.Api.Common;
using PoFightJudge.Api.Features.Profiles;
using PoFightJudge.Integration.Support;
using PoFightJudge.Shared.Identifiers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PoFightJudge.Integration;

[Collection(AzuriteCollection.Name)]
public class ProfileImageServiceTests(AzuriteFixture azurite)
{
    private ProfileImageService Sut()
    {
        Skip.IfNot(azurite.IsAvailable, azurite.Unavailable);
        return new ProfileImageService(azurite.Blobs, new StorageContainers("test-audio", "test-faces", "test-ttscache"));
    }

    [SkippableFact]
    public async Task Store_get_and_delete_round_trip_through_the_faces_container()
    {
        var sut = Sut();
        var id = ProfileId.From("IMG");
        using var image = new Image<Rgba32>(200, 300, new Rgba32(10, 200, 90));
        using var source = new MemoryStream();
        await image.SaveAsJpegAsync(source);
        source.Position = 0;

        (await sut.GetFaceAsync(id)).Should().BeNull("nothing stored yet");

        var stored = await sut.StoreFaceAsync(id, source);
        stored.IsSuccess.Should().BeTrue(stored.Error);
        stored.Value.Should().Be("IMG.png");

        var face = await sut.GetFaceAsync(id);
        face.Should().NotBeNull();
        face!.ContentType.Should().Be(ProfileImageService.ContentType);
        var info = Image.Identify(face.Bytes.Span);
        info.Width.Should().Be(512);
        info.Height.Should().Be(512);

        await sut.DeleteFaceAsync(id);
        (await sut.GetFaceAsync(id)).Should().BeNull();
        await sut.DeleteFaceAsync(id);
    }

    [SkippableFact]
    public async Task A_rejected_upload_stores_nothing()
    {
        var sut = Sut();
        var id = ProfileId.From("BAD");
        using var text = new MemoryStream("definitely not an image"u8.ToArray());

        var stored = await sut.StoreFaceAsync(id, text);

        stored.IsSuccess.Should().BeFalse();
        (await sut.GetFaceAsync(id)).Should().BeNull();
    }
}
