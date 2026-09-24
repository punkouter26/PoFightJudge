using Microsoft.Playwright;
using Xunit.Abstractions;

namespace PoFightJudge.E2EUI;

/// <summary>
/// Not a test so much as a camera: opt in with <c>SHOTS</c> (a folder) and it saves a full-page PNG of each route,
/// against the same in-memory, fake-AI host the rest of this suite drives. A cloud session cannot be browsed, so this
/// is how a change gets looked at before it is deployed. <c>ROUTES</c> (comma-separated names or paths) narrows it
/// to the pages a change touched. Skipped when <c>SHOTS</c> is unset, so the suite is unaffected.
/// </summary>
[Collection(AppCollection.Name)]
public sealed class ScreenshotRunner(AppFixture app, ITestOutputHelper output)
{
    /// <summary>Every page that renders without a match or a token in its URL. The one list to extend.</summary>
    public static readonly IReadOnlyList<(string Name, string Path)> Routes =
    [
        ("home", "/"),
        ("watch", "/watch"),
        ("one-player", "/1p"),
        ("fight", "/fight"),
        ("fighters", "/fighters"),
        ("profiles", "/profiles"),
        ("history", "/history"),
        ("health", "/health"),
        ("login", "/login"),
    ];

    [SkippableFact]
    public async Task Capture()
    {
        var folder = Environment.GetEnvironmentVariable("SHOTS");
        Skip.If(string.IsNullOrWhiteSpace(folder), "Set SHOTS to a folder to take screenshots.");
        Directory.CreateDirectory(folder!);

        var wanted = (Environment.GetEnvironmentVariable("ROUTES") ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var routes = wanted.Length == 0
            ? Routes
            : [.. Routes.Where(r => wanted.Contains(r.Name, StringComparer.OrdinalIgnoreCase) || wanted.Contains(r.Path, StringComparer.OrdinalIgnoreCase))];
        Skip.If(routes.Count == 0, $"ROUTES matched nothing. Known: {string.Join(", ", Routes.Select(r => r.Name))}");

        foreach (var (name, path) in routes)
        {
            var (context, page, errors) = await app.OpenAsync(AppFixture.Desktop, path);
            try
            {
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                await page.WaitForTimeoutAsync(1000); // animations and late renders settle
                var file = Path.Combine(folder!, name + ".png");
                await page.ScreenshotAsync(new PageScreenshotOptions { Path = file, FullPage = true });
                Report($"SHOT {name} {path} -> {file} ({errors.Count} errors)");
                foreach (var error in errors)
                {
                    Report($"  {name}: {error}");
                }
            }
            finally
            {
                await context.CloseAsync();
            }
        }
    }

    private void Report(string line)
    {
        output.WriteLine(line);
        Console.WriteLine(line);
    }
}
