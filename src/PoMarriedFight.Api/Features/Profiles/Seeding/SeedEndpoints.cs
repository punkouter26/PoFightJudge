using Carter;
using PoMarriedFight.Api.Common;
using PoMarriedFight.Shared;
using PoMarriedFight.Shared.Models;

namespace PoMarriedFight.Api.Features.Profiles.Seeding;

/// <summary>
/// <c>POST /api/seed/profiles</c>: writes the default cast (SPEC §7; admin gate in Production, any signed-in user elsewhere). Upsert semantics — re-running restores
/// a default persona that was edited, keeps any face already uploaded, and stores the bundled portrait for the
/// personas that ship with one.
/// </summary>
public sealed class SeedEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost(ApiRoutes.Seed.ProfilesUrl, SeedAsync).WithTags("Seed")
            .Produces<SeedResultDto>().Produces(StatusCodes.Status403Forbidden);

    private static async Task<IResult> SeedAsync(
        HttpContext http,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        IProfileRepository repo,
        IProfileImageService images,
        ILogger<SeedEndpoints> logger,
        CancellationToken ct)
    {
        if (!SeedAdminGate.IsSeedAdmin(http.User, configuration, environment.IsProduction()))
        {
            return Results.Forbid();
        }

        var initials = new List<string>();
        var faces = 0;
        foreach (var persona in SeedProfiles.All)
        {
            var profile = persona.ToDomain();
            var existing = await repo.GetByIdAsync(profile.Id, ct);
            profile.UpdateFacePic(existing?.FacePic);

            // The portraits are static web assets of the client (served from the same host), so the seed can read
            // them through the web root in Development and after publish alike; a host without them seeds faceless.
            var asset = environment.WebRootFileProvider.GetFileInfo(SeedProfiles.FaceAsset(profile.Initials));
            if (asset.Exists)
            {
                await using var stream = asset.CreateReadStream();
                var stored = await images.StoreFaceAsync(profile.Id, stream, ct);
                if (stored.IsSuccess)
                {
                    profile.UpdateFacePic(stored.Value);
                    faces++;
                }
                else
                {
                    logger.LogWarning("Seed portrait for {Initials} skipped: {Error}", profile.Initials, stored.Error);
                }
            }

            await repo.UpsertAsync(profile, ct);
            initials.Add(profile.Initials);
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Seeded {Count} profiles ({Faces} with a portrait) for {User}", initials.Count, faces, http.User.Identity?.Name);
        }

        return Results.Ok(new SeedResultDto(initials.Count, initials, faces));
    }
}
