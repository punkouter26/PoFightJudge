using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using PoFightJudge.Api.Features.Auth;
using PoFightJudge.Shared;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PoFightJudge.E2EAPI;

[Collection(ApiCollection.Name)]
public class ProfilesTests(ApiFactory factory)
{
    private HttpClient User(string id = "e2e-user", string? roles = null)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(FakeAuthOptions.UserHeader, id);
        if (roles is not null)
        {
            client.DefaultRequestHeaders.Add(FakeAuthOptions.RolesHeader, roles);
        }

        return client;
    }

    private static CreateProfileRequest Persona(string initials, ProfileRole role = ProfileRole.Husband) => new()
    {
        Initials = initials,
        Role = role,
        Name = "Sam",
        Likes = "coffee",
        Dislikes = "queues",
        CommonArguments = "chores",
        Philosophy = "keep it short",
        Patience = 30,
        TtsSettings = new TtsSettingsDto { VoiceName = "Puck" },
    };

    private static async Task<ByteArrayContent> PngAsync(int size = 64)
    {
        using var image = new Image<Rgba32>(size, size, new Rgba32(20, 120, 220));
        using var ms = new MemoryStream();
        await image.SaveAsPngAsync(ms);
        var content = new ByteArrayContent(ms.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return content;
    }

    [Fact]
    public async Task Create_read_update_delete_round_trip_with_the_documented_status_codes()
    {
        using var client = User();

        var created = await client.PostAsJsonAsync(ApiRoutes.Profiles.Base, Persona("e2a"));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        created.Headers.Location!.ToString().Should().EndWith(ApiRoutes.Profiles.ById(ProfileId.From("E2A")));
        var dto = (await created.Content.ReadFromJsonAsync<ProfileDto>())!;
        dto.Id.Should().Be(ProfileId.From("E2A"));
        dto.HasFace.Should().BeFalse();
        dto.Persona.Initials.Should().Be("E2A");
        dto.Persona.TtsSettings.VoiceName.Should().Be("Puck");

        (await client.PostAsJsonAsync(ApiRoutes.Profiles.Base, Persona("E2A"))).StatusCode.Should().Be(HttpStatusCode.Conflict, "initials are the key");

        var list = await client.GetFromJsonAsync<List<ProfileDto>>(ApiRoutes.Profiles.Base);
        list!.Select(p => p.Persona.Initials).Should().Contain("E2A");

        var update = Persona("E2A");
        update.Likes = "tea";
        update.TtsSettings.VoiceName = "Kore";
        var updated = await client.PutAsJsonAsync(ApiRoutes.Profiles.ById(dto.Id), update);
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = (await updated.Content.ReadFromJsonAsync<ProfileDto>())!;
        after.Persona.Likes.Should().Be("tea");
        after.Persona.TtsSettings.VoiceName.Should().Be("Charon", "a wife voice on a husband is coerced back to the role default");

        var renamed = Persona("E2B");
        (await client.PutAsJsonAsync(ApiRoutes.Profiles.ById(dto.Id), renamed)).StatusCode.Should().Be(HttpStatusCode.BadRequest, "initials cannot change on update");
        (await client.PutAsJsonAsync(ApiRoutes.Profiles.ById(ProfileId.From("NOP")), Persona("NOP"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync(ApiRoutes.Profiles.ById(ProfileId.From("NOP")))).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await client.DeleteAsync(ApiRoutes.Profiles.ById(dto.Id))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync(ApiRoutes.Profiles.ById(dto.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.DeleteAsync(ApiRoutes.Profiles.ById(dto.Id))).StatusCode.Should().Be(HttpStatusCode.NoContent, "delete is idempotent");
    }

    [Fact]
    public async Task Invalid_bodies_come_back_as_validation_problems_naming_the_field()
    {
        using var client = User();
        var bad = Persona("SELF");
        bad.Patience = 101;
        bad.TtsSettings.Pitch = 9;

        var response = await client.PostAsJsonAsync(ApiRoutes.Profiles.Base, bad);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = (await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>())!;
        problem.Errors.Keys.Should().Contain(["Initials", "Patience", "TtsSettings.Pitch"]);
        problem.Errors["Initials"].Single().Should().Contain("1–3 letters or digits");
    }

    [Fact]
    public async Task Profiles_require_a_signed_in_user()
    {
        using var anonymous = factory.CreateClient();

        (await anonymous.GetAsync(ApiRoutes.Profiles.Base)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync(ApiRoutes.Profiles.Base, Persona("ANO"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
