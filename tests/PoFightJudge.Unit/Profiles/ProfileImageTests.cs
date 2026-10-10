using PoFightJudge.Api.Features.Profiles;
using PoFightJudge.TestSupport;

namespace PoFightJudge.Unit.Profiles;

public class ProfileImageTests
{
    /// <summary>The server decodes nothing: a 512 px square PNG is kept byte for byte, and anything else is refused with a reason.</summary>
    [Theory]
    [InlineData("square", null)]
    [InlineData("empty", "No image")]
    [InlineData("text", "Not a PNG")]
    [InlineData("small", "512")]
    [InlineData("huge", "larger than")]
    public async Task Only_a_512_square_png_is_kept(string kind, string? refusal)
    {
        var bytes = kind switch
        {
            "square" => TestPng.Solid(ProfileImageService.Size, ProfileImageService.Size),
            "empty" => [],
            "text" => "definitely not an image, however long it goes on"u8.ToArray(),
            "small" => TestPng.Solid(64, 64),
            _ => new byte[ProfileImageService.MaxUploadBytes + 1],
        };
        using var body = new MemoryStream(bytes);

        var read = await ProfileImageService.ReadPngAsync(body, CancellationToken.None);

        if (refusal is null)
        {
            read.IsSuccess.Should().BeTrue(read.Error);
            read.Value.Should().Equal(bytes);
        }
        else
        {
            read.IsSuccess.Should().BeFalse();
            read.Error.Should().Contain(refusal);
        }
    }
}
