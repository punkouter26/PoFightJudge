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
/// the finished argument is captured as <c>watch-play</c>; then one fight is played on the fake microphone and
/// captured as <c>fight-live</c>, <c>verdict-reveal</c> and <c>verdict</c>.
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

        await FightAsync(folder);
    }

    /// <summary>
    /// One whole fight on the fake microphone and the scripted host, so the live stage and the ruling it hands over
    /// to are photographed too: <c>fight-live</c> mid-argument, <c>verdict-reveal</c> while the ruling is still
    /// arriving, and <c>verdict</c> once it has settled.
    /// </summary>
    private async Task FightAsync(string folder)
    {
        var (context, page, errors) = await app.OpenAsync(AppFixture.Desktop, "/2p", user: "e2e-shots");
        try
        {
            foreach (var (name, tag) in new[] { ("player1Tag", "AL"), ("player2Tag", "SM") })
            {
                var input = page.Locator($"div.tag-input[data-name={name}] input");
                await input.WaitForAsync(new() { Timeout = 30_000 });
                await input.FillAsync(tag);
                await input.PressAsync("Tab", new() { Delay = 20 });
            }

            await page.Locator("button.rz-steps-next").ClickAsync();
            await page.Locator("input[name=topic]").FillAsync("who does the dishes");
            await AppFixture.ToTheLastStepAsync(page);
            await page.GetByRole(AriaRole.Button, new() { Name = "Start the fight" }).ClickAsync();

            await page.WaitForFunctionAsync("() => document.querySelectorAll('article.caption').length > 1", null, new() { Timeout = 90_000 });
            await ShootAsync(page, folder, "fight-live", "/fight/{id}", errors);

            // The scripted host is never talked over, so the cut-in is forced.
            await MomentAsync(page, folder, "fight-glitch", "() => PoFx.burst('glitch')", 60);

            await page.Locator("section.ruling").WaitForAsync(new() { Timeout = 180_000 });
            await page.WaitForTimeoutAsync(700);
            await ShootAsync(page, folder, "verdict-reveal", "/verdict/{id}", errors);
            await page.WaitForTimeoutAsync(5000);
            await ShootAsync(page, folder, "verdict", "/verdict/{id}", errors);
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    /// <summary>
    /// An effect too quick to catch in passing, frozen part-way: the script starts it, every CSS animation on the page
    /// is paused <paramref name="atMs"/> in, and the window is shot. Canvas effects have no clock to stop; they are
    /// whatever frame the shot lands on.
    /// </summary>
    private async Task MomentAsync(IPage page, string folder, string name, string script, int atMs)
    {
        await page.EvaluateAsync(script);
        await page.EvaluateAsync("t => document.getAnimations().forEach(a => { a.pause(); a.currentTime = t; })", atMs);
        var file = Path.Combine(folder, name + ".png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = file });
        await page.EvaluateAsync("() => document.getAnimations().forEach(a => a.play())");
        Report($"SHOT {name} (moment, {atMs} ms) -> {file}");
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
