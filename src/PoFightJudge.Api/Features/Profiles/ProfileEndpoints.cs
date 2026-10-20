using System.Security.Claims;
using Carter;
using PoFightJudge.Api.Common;
using PoFightJudge.Api.Features.Ai;
using PoFightJudge.Api.Features.Auth;
using PoFightJudge.Api.Features.Records;
using PoFightJudge.Api.Features.Voice;
using PoFightJudge.Api.Features.Watch;
using PoFightJudge.Shared;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Api.Features.Profiles;

/// <summary>
/// The cast, read-only. A profile is never written by hand: it is the pre-made cast (seeded at startup, restored by
/// the seed endpoint) or written from somebody's play by <see cref="Fighters.FighterPersonaWriter"/>. Each one comes
/// back with its record, counted from result rows on read. Profiles are global (SPEC §6); every route needs a
/// signed-in user through the fallback policy except the face itself, which is fetched by <c>&lt;img&gt;</c>.
/// </summary>
public sealed class ProfileEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var profiles = app.MapGroup(ApiRoutes.Profiles.Base).WithTags("Profiles");

        profiles.MapGet(string.Empty, ListAsync).Produces<List<ProfileDto>>();
        profiles.MapGet(ApiRoutes.Profiles.ByIdSegment, GetAsync).Produces<ProfileDto>().Produces(StatusCodes.Status404NotFound);
        profiles.MapPost(ApiRoutes.Profiles.PreviewLineSegment, PreviewLineAsync).RequireRateLimiting(WatchServiceExtensions.AiPolicy).WithValidation<CreateProfileRequest>().Produces<PreviewLineResponse>();

        profiles.MapGet(ApiRoutes.Profiles.FaceSegment, GetFaceAsync).AllowAnonymous()
            .Produces(StatusCodes.Status200OK, contentType: ProfileImageService.ContentType).Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ListAsync(ClaimsPrincipal user, IProfileRepository repo, IWatchResultRepository watches, IFighterResultRepository fights, CancellationToken ct)
    {
        var records = await RecordsAsync(user, watches, fights, ct);
        return Results.Ok((await repo.GetAllAsync(ct)).Select(p => WithRecord(p, records)).ToList());
    }

    private static async Task<IResult> GetAsync(ProfileId id, ClaimsPrincipal user, IProfileRepository repo, IWatchResultRepository watches, IFighterResultRepository fights, CancellationToken ct) =>
        await repo.GetByIdAsync(id, ct) is { } profile
            ? Results.Ok(WithRecord(profile, await RecordsAsync(user, watches, fights, ct)))
            : Results.NotFound();

    /// <summary>The cast's records from the watches they were argued in; people's from the fights this account ran.</summary>
    private static async Task<(IReadOnlyDictionary<string, ProfileRecordDto> Cast, IReadOnlyDictionary<string, ProfileRecordDto> People)> RecordsAsync(
        ClaimsPrincipal user, IWatchResultRepository watches, IFighterResultRepository fights, CancellationToken ct) =>
        (ProfileRecords.From(await watches.ListAllAsync(ct)), ProfileRecords.From(await fights.ListAllAsync(user.UserId(), ct)));

    /// <summary>A persona read from somebody's play is that person, so its record is theirs; the cast's is the watches'.</summary>
    private static ProfileDto WithRecord(Profile profile, (IReadOnlyDictionary<string, ProfileRecordDto> Cast, IReadOnlyDictionary<string, ProfileRecordDto> People) records) =>
        profile.ToDto() with
        {
            Record = (profile.FromFights ? records.People : records.Cast).GetValueOrDefault(profile.Initials, ProfileRecordDto.None),
        };

    /// <summary>
    /// One in-character line from the persona, spoken in its own voice — what the speaker button on a profile card
    /// plays. Never persisted. The
    /// prompt here is deliberately minimal; the real round prompts belong to the WATCH slice.
    /// </summary>
    private static async Task<IResult> PreviewLineAsync(CreateProfileRequest persona, IGeminiText gemini, GeminiModelOptions models, CachingTtsService voice, CancellationToken ct)
    {
        var raw = await gemini.GenerateAsync(new GeminiTextRequest(PreviewPrompt(persona), models.Round, "preview", PreviewSchema, MaxOutputTokens: 256, ThinkingLevel: "minimal"), ct);
        var (line, mood) = ParsePreview(raw);
        var settings = persona.TtsSettings.ToDomain().Normalize(persona.Role);
        var audio = await voice.GenerateTtsAsync(line, settings, ct);
        return Results.Ok(new PreviewLineResponse(line, mood, new TtsAudioDto(audio.Base64, audio.Format)));
    }

    private static GeminiPrompt PreviewPrompt(CreateProfileRequest persona) => new(
        System: "You write one line of dialogue for a satirical married-couple argument show. One sentence, in character, "
            + "spoken out loud to their spouse mid-argument. No stage directions, no quotation marks. " + PromptText.DataNotice,
        User: $"""
            The speaker is the {persona.Role.ToString().ToLowerInvariant()} {PromptText.Inline(persona.Name ?? persona.Initials, 60)}.
            Likes: {PromptText.Inline(persona.Likes, 600)}
            Dislikes: {PromptText.Inline(persona.Dislikes, 600)}
            They usually argue about: {PromptText.Inline(persona.CommonArguments, 600)}
            Their philosophy: {PromptText.Inline(persona.Philosophy, 600)}

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

        return (WatchAi.Readable(raw, WatchAi.ExtractLinePrefix(raw)), "angry");
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
}
