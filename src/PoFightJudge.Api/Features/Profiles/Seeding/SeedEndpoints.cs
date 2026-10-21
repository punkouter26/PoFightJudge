using Carter;
using PoFightJudge.Api.Common;
using PoFightJudge.Shared;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Api.Features.Profiles.Seeding;

/// <summary>
/// <c>POST /api/seed/profiles</c>, behind "Load default cast": a reset to the built-in personas (SPEC §7; admin gate in
/// Production, any signed-in user elsewhere). Every other profile is removed with its portrait, the ones written
/// from play included; those come back after that person's next match, and their records are not touched. Then each
/// built-in persona is written as it ships, with its bundled portrait.
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

        // Everything that is not a built-in persona goes first, so what is left is exactly the cast that ships.
        var builtIn = SeedProfiles.All.Select(p => Initials.Normalize(p.Initials)).ToHashSet(StringComparer.Ordinal);
        var removed = 0;
        foreach (var other in (await repo.GetAllAsync(ct)).Where(p => !builtIn.Contains(p.Initials)))
        {
            await images.DeleteFaceAsync(other.Id, ct);
            await repo.DeleteAsync(other.Id, ct);
            removed++;
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
            logger.LogInformation("Seeded {Count} profiles ({Faces} with a portrait) and removed {Removed} others for {User}", initials.Count, faces, removed, http.User.Identity?.Name);
        }

        return Results.Ok(new SeedResultDto(initials.Count, initials, faces, removed));
    }
}
