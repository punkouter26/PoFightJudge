using Carter;
using Microsoft.AspNetCore.Mvc;
using PoMarriedFight.Api.Common;
using PoMarriedFight.Api.Features.Ai;
using PoMarriedFight.Api.Features.Records;
using PoMarriedFight.Api.Features.Voice;
using PoMarriedFight.Shared;
using PoMarriedFight.Shared.Identifiers;
using PoMarriedFight.Shared.Models;

namespace PoMarriedFight.Api.Features.Profiles;

/// <summary>
/// WATCH persona CRUD plus the face image. Profiles are global (SPEC §6), so there is no ownership check; every route
/// needs a signed-in user through the fallback policy except the face itself, which is fetched by <c>&lt;img&gt;</c>
/// without a bearer token.
/// </summary>
public sealed class ProfileEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var profiles = app.MapGroup(ApiRoutes.Profiles.Base).WithTags("Profiles");

        profiles.MapGet(string.Empty, ListAsync).Produces<List<ProfileDto>>();
        profiles.MapGet(ApiRoutes.Profiles.ByIdSegment, GetAsync).Produces<ProfileDto>().Produces(StatusCodes.Status404NotFound);

        profiles.MapGet(ApiRoutes.Profiles.RecordSegment, RecordAsync)
            .Produces<ProfileRecordDto>()
            .Produces(StatusCodes.Status404NotFound);
        profiles.MapPost(string.Empty, CreateAsync).WithValidation<CreateProfileRequest>()
            .Produces<ProfileDto>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status409Conflict);
        profiles.MapPut(ApiRoutes.Profiles.ByIdSegment, UpdateAsync).WithValidation<CreateProfileRequest>()
            .Produces<ProfileDto>().Produces(StatusCodes.Status404NotFound);
        profiles.MapDelete(ApiRoutes.Profiles.ByIdSegment, DeleteAsync).Produces(StatusCodes.Status204NoContent);
        profiles.MapPost(ApiRoutes.Profiles.GenerateSegment, GenerateAsync).Produces<CreateProfileRequest>().ProducesValidationProblem();
        profiles.MapPost(ApiRoutes.Profiles.PreviewLineSegment, PreviewLineAsync).WithValidation<CreateProfileRequest>().Produces<PreviewLineResponse>();

        profiles.MapGet(ApiRoutes.Profiles.FaceSegment, GetFaceAsync).AllowAnonymous()
            .Produces(StatusCodes.Status200OK, contentType: ProfileImageService.ContentType).Produces(StatusCodes.Status404NotFound);
        profiles.MapPost(ApiRoutes.Profiles.FaceSegment, UploadFaceAsync)
            .WithMetadata(new RequestSizeLimitAttribute(ProfileImageService.MaxUploadBytes))
            .Produces<ProfileDto>().ProducesValidationProblem().ProducesProblem(StatusCodes.Status415UnsupportedMediaType).Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ListAsync(IProfileRepository repo, CancellationToken ct) =>
        Results.Ok((await repo.GetAllAsync(ct)).Select(p => p.ToDto()).ToList());

    /// <summary>
    /// How this persona has done in the watches it has argued in, computed from its result rows. A persona with no
    /// matches yet has an empty record rather than none: it exists, it just has not argued.
    /// </summary>
    private static async Task<IResult> RecordAsync(
        ProfileId id,
        IProfileRepository profiles,
        IWatchResultRepository results,
        CancellationToken ct)
    {
        if (await profiles.GetByIdAsync(id, ct) is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(ProfileStatsBuilder.Build(id.Value, await results.ListForAsync(id.Value, ct)));
    }

    private static async Task<IResult> GetAsync(ProfileId id, IProfileRepository repo, CancellationToken ct) =>
        await repo.GetByIdAsync(id, ct) is { } profile ? Results.Ok(profile.ToDto()) : Results.NotFound();

    /// <summary>Create must not clobber: initials are the key, and an upsert here silently replaced an existing persona in PoMarriedLife.</summary>
    private static async Task<IResult> CreateAsync(CreateProfileRequest request, IProfileRepository repo, CancellationToken ct)
    {
        var profile = request.ToDomain();
        if (await repo.GetByIdAsync(profile.Id, ct) is not null)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: $"A profile with initials {profile.Initials} already exists.");
        }

        await repo.UpsertAsync(profile, ct);
        return Results.Created(ApiRoutes.Profiles.ById(profile.Id), profile.ToDto());
    }

    private static async Task<IResult> UpdateAsync(ProfileId id, CreateProfileRequest request, IProfileRepository repo, CancellationToken ct)
    {
        var existing = await repo.GetByIdAsync(id, ct);
        if (existing is null)
        {
            return Results.NotFound();
        }

        if (!string.Equals(Initials.Normalize(request.Initials), id.Value, StringComparison.Ordinal))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [nameof(CreateProfileRequest.Initials)] = ["Initials are the key and cannot change; create a new profile instead."],
            });
        }

        var profile = request.ToDomain();
        profile.UpdateFacePic(existing.FacePic); // the face has its own endpoint; an edit never drops it
        await repo.UpsertAsync(profile, ct);
        return Results.Ok(profile.ToDto());
    }

    /// <summary>An unsaved draft for the editor: the user reviews it, edits, and only then creates the profile.</summary>
    private static async Task<IResult> GenerateAsync(string? role, IProfileGenerator generator, IProfileRepository repo, CancellationToken ct)
    {
        if (!Enum.TryParse<ProfileRole>(role, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["role"] = ["role must be Husband or Wife."] });
        }

        var existing = await repo.GetAllAsync(ct);
        return Results.Ok(await generator.GenerateAsync(parsed, existing, ct));
    }

    /// <summary>
    /// One in-character line from the persona as it stands in the editor, spoken in its own voice — the quickest way
    /// to hear what the sliders and the Fish reference id actually do before committing them. Never persisted. The
    /// prompt here is deliberately minimal; the real round prompts belong to the WATCH slice.
    /// </summary>
    private static async Task<IResult> PreviewLineAsync(CreateProfileRequest persona, IGeminiText gemini, GeminiModelOptions models, ITtsService voice, CancellationToken ct)
    {
        var raw = await gemini.GenerateAsync(new GeminiTextRequest(PreviewPrompt(persona), models.Round, "preview", PreviewSchema, MaxOutputTokens: 256, ThinkingLevel: "minimal"), ct);
        var (line, mood) = ParsePreview(raw);
        var settings = persona.TtsSettings.ToDomain().Normalize(persona.Role);
        var audio = await voice.GenerateTtsAsync(line, settings, ct);
        return Results.Ok(new PreviewLineResponse(line, mood, new TtsAudioDto(audio.Base64, audio.Format)));
    }

    private static GeminiPrompt PreviewPrompt(CreateProfileRequest persona) => new(
        System: "You write one line of dialogue for a satirical married-couple argument show. One sentence, in character, "
            + "spoken out loud to their spouse mid-argument. No stage directions, no quotation marks.",
        User: $"""
            The speaker is the {persona.Role.ToString().ToLowerInvariant()} {persona.Name ?? persona.Initials}.
            Likes: {persona.Likes}
            Dislikes: {persona.Dislikes}
            They usually argue about: {persona.CommonArguments}
            Their philosophy: {persona.Philosophy}

            Write their next line.
            """);

    private static readonly System.Text.Json.Nodes.JsonObject PreviewSchema = new()
    {
        ["type"] = "object",
        ["properties"] = new System.Text.Json.Nodes.JsonObject
        {
            ["line"] = new System.Text.Json.Nodes.JsonObject { ["type"] = "string" },
            ["mood"] = new System.Text.Json.Nodes.JsonObject
            {
                ["type"] = "string",
                ["enum"] = new System.Text.Json.Nodes.JsonArray("angry", "peaceful", "embarrassed", "hateful", "humble"),
            },
        },
        ["required"] = new System.Text.Json.Nodes.JsonArray("line", "mood"),
    };

    /// <summary>The model answers against the schema, but a fenced or partial answer still has to read as a line.</summary>
    internal static (string Line, string Mood) ParsePreview(string raw)
    {
        try
        {
            var start = raw.IndexOf('{', StringComparison.Ordinal);
            var end = raw.LastIndexOf('}');
            if (start >= 0 && end > start && System.Text.Json.Nodes.JsonNode.Parse(raw[start..(end + 1)]) is System.Text.Json.Nodes.JsonObject parsed)
            {
                var line = parsed["line"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(line))
                {
                    return (line.Trim(), parsed["mood"]?.GetValue<string>() ?? "angry");
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Not JSON at all — treat the whole answer as the line.
        }

        return (raw.Trim(), "angry");
    }

    private static async Task<IResult> DeleteAsync(ProfileId id, IProfileRepository repo, IProfileImageService images, CancellationToken ct)
    {
        await images.DeleteFaceAsync(id, ct);
        await repo.DeleteAsync(id, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> GetFaceAsync(ProfileId id, IProfileImageService images, HttpContext http, CancellationToken ct)
    {
        var face = await images.GetFaceAsync(id, ct);
        if (face is null)
        {
            return Results.NotFound();
        }

        // Anonymous and same-origin as the SPA: the browser must never sniff a stored blob into something script-capable.
        http.Response.Headers.XContentTypeOptions = "nosniff";
        return Results.Bytes(face.Bytes, face.ContentType);
    }

    /// <summary>The body is the image itself (any raster type); it is re-encoded before anything is stored.</summary>
    private static async Task<IResult> UploadFaceAsync(ProfileId id, HttpRequest request, IProfileRepository repo, IProfileImageService images, CancellationToken ct)
    {
        if (request.ContentType is not { } type || !type.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Problem(statusCode: StatusCodes.Status415UnsupportedMediaType, title: "Send the image bytes with an image/* content type.");
        }

        var profile = await repo.GetByIdAsync(id, ct);
        if (profile is null)
        {
            return Results.NotFound();
        }

        var stored = await images.StoreFaceAsync(id, request.Body, ct);
        if (!stored.IsSuccess)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["face"] = [stored.Error!] });
        }

        profile.UpdateFacePic(stored.Value);
        await repo.UpsertAsync(profile, ct);
        return Results.Ok(profile.ToDto());
    }
}
