using PoMarriedFight.Api.Features.Profiles;
using PoMarriedFight.Shared.Identifiers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace PoMarriedFight.Unit.Profiles;

public class ProfileImageServiceTests
{
    [Theory]
    [InlineData(300, 200)]
    [InlineData(64, 64)]
    [InlineData(900, 1600)]
    public async Task Any_raster_becomes_a_512_square_png(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(200, 30, 30));
        using var source = new MemoryStream();
        await image.SaveAsJpegAsync(source);
        source.Position = 0;

        var outcome = await ProfileImageService.NormalizeAsync(source, CancellationToken.None);

        outcome.IsSuccess.Should().BeTrue(outcome.Error);
        var info = Image.Identify(outcome.Value);
        info.Width.Should().Be(ProfileImageService.Size);
        info.Height.Should().Be(ProfileImageService.Size);
        info.Metadata.DecodedImageFormat.Should().Be(PngFormat.Instance);
    }

    [Fact]
    public async Task Markup_and_empty_bodies_are_not_images()
    {
        using var svg = new MemoryStream("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"u8.ToArray());
        using var empty = new MemoryStream();

        var svgOutcome = await ProfileImageService.NormalizeAsync(svg, CancellationToken.None);
        var emptyOutcome = await ProfileImageService.NormalizeAsync(empty, CancellationToken.None);

        svgOutcome.IsSuccess.Should().BeFalse();
        svgOutcome.Error.Should().Contain("image");
        emptyOutcome.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Uploads_over_the_cap_are_refused_before_decoding()
    {
        using var huge = new MemoryStream(new byte[ProfileImageService.MaxUploadBytes + 1]);

        var outcome = await ProfileImageService.NormalizeAsync(huge, CancellationToken.None);

        outcome.IsSuccess.Should().BeFalse();
        outcome.Error.Should().Contain("MB");
    }

    [Fact]
    public void Blob_names_are_the_initials_as_png()
    {
        ProfileImageService.BlobName(ProfileId.From("MAH")).Should().Be("MAH.png");
    }
}
