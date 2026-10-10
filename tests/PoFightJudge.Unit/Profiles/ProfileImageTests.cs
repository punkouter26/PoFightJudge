using PoFightJudge.Api.Features.Profiles;
using PoFightJudge.TestSupport;

namespace PoFightJudge.Unit.Profiles;

public class ProfileImageTests
{
    [Fact]
    public async Task A_512_square_png_is_kept_byte_for_byte()
    {
        var png = TestPng.Solid(ProfileImageService.Size, ProfileImageService.Size);
        using var body = new MemoryStream(png);

        var read = await ProfileImageService.ReadPngAsync(body, CancellationToken.None);

        read.IsSuccess.Should().BeTrue(read.Error);
        read.Value.Should().Equal(png);
    }

    [Theory]
    [InlineData("empty", "No image")]
    [InlineData("text", "Not a PNG")]
    [InlineData("small", "512")]
    [InlineData("huge", "larger than")]
    public async Task Anything_else_is_refused_with_a_reason(string kind, string reason)
    {
        var bytes = kind switch
        {
            "empty" => [],
            "text" => "definitely not an image, however long it goes on"u8.ToArray(),
            "small" => TestPng.Solid(64, 64),
            _ => new byte[ProfileImageService.MaxUploadBytes + 1],
        };
        using var body = new MemoryStream(bytes);

        var read = await ProfileImageService.ReadPngAsync(body, CancellationToken.None);

        read.IsSuccess.Should().BeFalse();
        read.Error.Should().Contain(reason);
    }
}
