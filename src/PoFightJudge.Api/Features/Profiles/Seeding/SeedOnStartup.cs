using PoFightJudge.Shared.Models;

namespace PoFightJudge.Api.Features.Profiles.Seeding;

/// <summary>
/// Puts any missing member of the default cast into the table when the app starts, so a persona added to
/// <see cref="SeedProfiles"/> in code is on the site after the next deploy without anybody signing in to press a
/// button.
/// </summary>
/// <remarks>
/// It adds and it never overwrites, which is the whole difference between this and <c>POST /api/seed/profiles</c>.
/// That endpoint is a deliberate, admin-triggered restore and upserts on purpose; this runs unattended on every
/// start, and an upsert here would quietly revert anybody's edit to a seeded persona each time the app was
/// deployed. A background service rather than a blocking startup task: a storage account that is slow to answer
/// must delay the cast, not the site.
/// </remarks>
public sealed partial class SeedOnStartup(
    IServiceProvider services,
    IWebHostEnvironment environment,
    ILogger<SeedOnStartup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = services.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IProfileRepository>();
            var images = scope.ServiceProvider.GetRequiredService<IProfileImageService>();

            var added = new List<string>();
            foreach (var persona in SeedProfiles.All)
            {
                var profile = persona.ToDomain();
                if (await repo.GetByIdAsync(profile.Id, stoppingToken) is not null)
                {
                    // Already known, however different it now is from the code. Somebody may have edited it.
                    continue;
                }

                var asset = environment.WebRootFileProvider.GetFileInfo(SeedProfiles.FaceAsset(profile.Initials));
                if (asset.Exists)
                {
                    await using var stream = asset.CreateReadStream();
                    var stored = await images.StoreFaceAsync(profile.Id, stream, stoppingToken);
                    if (stored.IsSuccess)
                    {
                        profile.UpdateFacePic(stored.Value);
                    }
                }

                await repo.UpsertAsync(profile, stoppingToken);
                added.Add(profile.Initials);
            }

            if (added.Count > 0)
            {
                // Joined into a local rather than passed as an argument: CA1873 reads any call in the argument list
                // as work that might be wasted, and ten initials is not worth arguing with it over.
                var names = string.Join(", ", added);
                LogSeeded(logger, added.Count, names);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A cast that failed to arrive is a smaller problem than a site that will not start because of it.
            LogFailed(logger, ex);
        }
    }

    [LoggerMessage(EventId = 1301, Level = LogLevel.Information, Message = "Seeded {Count} missing persona(s) on startup: {Initials}")]
    private static partial void LogSeeded(ILogger logger, int count, string initials);

    [LoggerMessage(EventId = 1302, Level = LogLevel.Warning, Message = "Startup persona seeding failed; the site is unaffected and POST /api/seed/profiles still works")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
