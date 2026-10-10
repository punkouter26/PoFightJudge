using Microsoft.Playwright;
using Xunit.Abstractions;

namespace PoFightJudge.E2EUI;

/// <summary>
/// Not a test so much as a camera: opt in with <c>SHOTS</c> (a folder) and it saves a full-page PNG of each route,
/// against the same in-memory, fake-AI host the rest of this suite drives. A cloud session cannot be browsed, so this
/// is how a change gets looked at before it is deployed. <c>ROUTES</c> (comma-separated names or paths) narrows it
/// to the pages a change touched. Skipped when <c>SHOTS</c> is unset, so the suite is unaffected.
/// <c>VIEWPORTS=desktop,phone</c> takes each page at both sizes (a phone shot is named <c>-phone</c>), and
/// <c>SEED=1</c> loads the default cast and plays one watch through first, so the lists have something in them and
/// the finished argument is captured as <c>watch-play</c>.
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

        var sizes = (Environment.GetEnvironmentVariable("VIEWPORTS") ?? "desktop")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(v => string.Equals(v, "phone", StringComparison.OrdinalIgnoreCase) ? (Suffix: "-phone", Size: AppFixture.Mobile) : (Suffix: string.Empty, Size: AppFixture.Desktop))
            .ToList();

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SEED")))
        {
            await SeedAsync(folder!);
        }

        foreach (var (suffix, size) in sizes)
        {
            foreach (var (name, path) in routes)
            {
                var (context, page, errors) = await app.OpenAsync(size, path);
                try
                {
                    await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                    await page.WaitForTimeoutAsync(1000); // animations and late renders settle
                    await ShootAsync(page, folder!, name + suffix, path, errors);
                }
                finally
                {
                    await context.CloseAsync();
                }
            }
        }
    }

    /// <summary>The default cast, and one whole argument between two of them, so no page is photographed empty.</summary>
    private async Task SeedAsync(string folder)
    {
        var (context, page, errors) = await app.OpenAsync(AppFixture.Desktop, "/profiles");
        try
        {
            var load = page.GetByRole(AriaRole.Button, new() { Name = "Load default cast" });
            await load.First.WaitForAsync(new() { Timeout = 30_000 });
            await load.First.ClickAsync();
            var confirm = page.Locator(".rz-dialog");
            await confirm.WaitForAsync(new() { Timeout = 10_000 });
            await confirm.GetByRole(AriaRole.Button, new() { Name = "Load cast" }).ClickAsync();
            await page.WaitForFunctionAsync("() => document.querySelectorAll('article.profile').length >= 8", null, new() { Timeout = 30_000 });

            await page.GotoAsync($"{app.BaseUrl}/cpu");
            await page.Locator("div.picker").First.WaitForAsync(new() { Timeout = 30_000 });
            for (var side = 0; side < 2; side++)
            {
                await page.Locator("div.picker").Nth(side).Locator(".rz-dropdown").ClickAsync();
                var panel = page.Locator(".rz-dropdown-panel:visible");
                await panel.WaitForAsync(new() { Timeout = 10_000 });
                await panel.Locator(".rz-dropdown-item").First.ClickAsync();
                await panel.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
            }

            await AppFixture.ToTheLastStepAsync(page);
            await page.GetByRole(AriaRole.Button, new() { Name = "Start the argument" }).ClickAsync();
            await page.Locator("section.verdict").WaitForAsync(new() { Timeout = 150_000 });
            await page.WaitForTimeoutAsync(1500);
            await ShootAsync(page, folder, "watch-play", "/watch/play", errors);
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    private async Task ShootAsync(IPage page, string folder, string name, string path, List<string> errors)
    {
        // The icons are a font: a page photographed before it arrives shows their names spelled out instead.
        // "Ready" only covers faces something has asked for so far, so each declared face is loaded by hand first.
        await page.EvaluateAsync("async () => { await Promise.allSettled([...document.fonts].map(f => f.load())); await document.fonts.ready; }");
        await page.WaitForTimeoutAsync(300);
        var icons = await page.EvaluateAsync<string>("() => [...document.fonts].filter(f => f.family.includes('Material')).map(f => f.status).join(',') + ' / ' + getComputedStyle(document.querySelector('.rzi') ?? document.body).fontFamily");
        Report($"  {name}: icon font {icons}");
        var file = Path.Combine(folder, name + ".png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = file, FullPage = true });
        Report($"SHOT {name} {path} -> {file} ({errors.Count} errors)");
        foreach (var error in errors)
        {
            Report($"  {name}: {error}");
        }
    }

    private void Report(string line)
    {
        output.WriteLine(line);
        Console.WriteLine(line);
    }
}
