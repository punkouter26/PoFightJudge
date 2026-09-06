using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using PoMarriedFight.Api.Features.Auth;
using PoMarriedFight.Shared;
using PoMarriedFight.Shared.Identifiers;
using PoMarriedFight.Shared.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PoMarriedFight.E2EAPI;

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
    public async Task Faces_are_uploaded_as_raw_images_and_served_anonymously_as_png()
    {
        using var client = User();
        using var anonymous = factory.CreateClient();
        var id = ProfileId.From("E2F");
        (await client.PostAsJsonAsync(ApiRoutes.Profiles.Base, Persona("E2F", ProfileRole.Wife))).StatusCode.Should().Be(HttpStatusCode.Created);

        (await anonymous.GetAsync(ApiRoutes.Profiles.Face(id))).StatusCode.Should().Be(HttpStatusCode.NotFound, "no face yet");

        using (var png = await PngAsync())
        {
            var upload = await client.PostAsync(ApiRoutes.Profiles.Face(id), png);
            upload.StatusCode.Should().Be(HttpStatusCode.OK);
            (await upload.Content.ReadFromJsonAsync<ProfileDto>())!.HasFace.Should().BeTrue();
        }

        var face = await anonymous.GetAsync(ApiRoutes.Profiles.Face(id));
        face.StatusCode.Should().Be(HttpStatusCode.OK);
        face.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        face.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        Image.Identify(await face.Content.ReadAsByteArrayAsync()).Width.Should().Be(512);

        (await client.GetFromJsonAsync<ProfileDto>(ApiRoutes.Profiles.ById(id)))!.HasFace.Should().BeTrue();

        using var text = new StringContent("not an image");
        (await client.PostAsync(ApiRoutes.Profiles.Face(id), text)).StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        using var garbage = new ByteArrayContent([1, 2, 3, 4]);
        garbage.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        (await client.PostAsync(ApiRoutes.Profiles.Face(id), garbage)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var orphan = await PngAsync();
        (await client.PostAsync(ApiRoutes.Profiles.Face(ProfileId.From("NOP")), orphan)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await client.DeleteAsync(ApiRoutes.Profiles.ById(id))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await anonymous.GetAsync(ApiRoutes.Profiles.Face(id))).StatusCode.Should().Be(HttpStatusCode.NotFound, "deleting the profile removes its face");
    }

    [Fact]
    public async Task Seeding_needs_a_signed_in_user_and_is_idempotent()
    {
        using var anonymous = factory.CreateClient();
        using var user = User("e2e-plain");
        using var admin = User("e2e-admin", roles: "Admin");

        (await anonymous.PostAsync(ApiRoutes.Seed.ProfilesUrl, null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await user.PostAsync(ApiRoutes.Seed.ProfilesUrl, null)).StatusCode.Should().Be(HttpStatusCode.OK, "outside Production any signed-in user may load the cast; the Production rules are unit-tested on the gate");

        var first = await admin.PostAsync(ApiRoutes.Seed.ProfilesUrl, null);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await first.Content.ReadFromJsonAsync<SeedResultDto>())!;
        result.Seeded.Should().Be(8);
        result.Initials.Should().Contain("MAH");

        (await admin.PostAsync(ApiRoutes.Seed.ProfilesUrl, null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var profiles = (await user.GetFromJsonAsync<List<ProfileDto>>(ApiRoutes.Profiles.Base))!;
        profiles.Count(p => string.Equals(p.Persona.Initials, "MAH", StringComparison.Ordinal)).Should().Be(1);
        profiles.Single(p => string.Equals(p.Persona.Initials, "DJT", StringComparison.Ordinal)).Persona.TtsSettings.FishReferenceId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Profiles_require_a_signed_in_user()
    {
        using var anonymous = factory.CreateClient();

        (await anonymous.GetAsync(ApiRoutes.Profiles.Base)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync(ApiRoutes.Profiles.Base, Persona("ANO"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Generate_returns_an_unsaved_valid_draft_for_the_role_and_rejects_an_unknown_role()
    {
        using var client = User();

        var response = await client.PostAsync(ApiRoutes.Profiles.Generate("wife"), null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var draft = (await response.Content.ReadFromJsonAsync<CreateProfileRequest>())!;
        draft.Role.Should().Be(ProfileRole.Wife);
        (await new PoMarriedFight.Shared.Validators.CreateProfileRequestValidator().ValidateAsync(draft)).IsValid.Should().BeTrue();
        draft.TtsSettings.VoiceName.Should().BeOneOf("Kore", "Zephyr");
        (await client.GetFromJsonAsync<List<ProfileDto>>(ApiRoutes.Profiles.Base))!
            .Should().NotContain(p => string.Equals(p.Persona.Initials, draft.Initials, StringComparison.Ordinal), "a draft is not persisted");

        (await client.PostAsync(ApiRoutes.Profiles.Generate("banana"), null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Preview_line_speaks_an_unsaved_persona_and_never_stores_it()
    {
        using var client = User();
        var persona = Persona("PRV", ProfileRole.Wife);
        persona.Name = "Vera Quill";

        var response = await client.PostAsJsonAsync(ApiRoutes.Profiles.PreviewLineUrl, persona);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var preview = (await response.Content.ReadFromJsonAsync<PreviewLineResponse>())!;
        preview.Line.Should().NotBeNullOrWhiteSpace();
        preview.Mood.Should().NotBeNullOrWhiteSpace();
        preview.Audio.IsEmpty.Should().BeFalse("the fake voice still speaks");
        preview.Audio.Format.Should().BeOneOf("pcm", "mp3");
        Convert.FromBase64String(preview.Audio.Base64).Length.Should().BeGreaterThan(0);

        (await client.GetFromJsonAsync<List<ProfileDto>>(ApiRoutes.Profiles.Base))!
            .Should().NotContain(p => string.Equals(p.Persona.Initials, "PRV", StringComparison.Ordinal), "a preview is not a profile");

        var invalid = Persona("PRV");
        invalid.Likes = string.Empty;
        (await client.PostAsJsonAsync(ApiRoutes.Profiles.PreviewLineUrl, invalid)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
