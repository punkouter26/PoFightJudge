using Microsoft.Playwright;

namespace PoMarriedFight.E2EUI;

/// <summary>
/// The WATCH cases the two flow tests do not reach: the person arguing first rather than second, a phone-sized
/// screen, and leaving mid-argument. These are the runs that produce the CP4 screenshots.
/// </summary>
[Collection(AppCollection.Name)]
public class WatchHardeningTests(AppFixture app)
{
    private static readonly string ShotsDirectory = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "screenshots");

    [SkippableFact]
    public async Task A_person_can_open_the_argument_and_the_screen_fits_a_phone()
    {
        var (context, page, errors) = await app.OpenAsync(AppFixture.Mobile, "/profiles", user: "e2e-opener");
        await using var _ = context;

        await SeedAsync(page);
        await page.GotoAsync($"{app.BaseUrl}/watch");
        await page.Locator("div.picker").First.WaitForAsync(new() { Timeout = 30_000 });

        // The person takes the husband's side, which opens the argument: the very first turn is theirs.
        await ChooseAsync(page, side: 0, "A person at the microphone");
        await page.Locator("input[name=husbandTag]").FillAsync("AB");
        await ChooseAsync(page, side: 1, "Kimberly");
        await page.GetByRole(AriaRole.Button, new() { Name = "Start the argument" }).ClickAsync();

        var turn = page.Locator("section.your-turn");
        await turn.WaitForAsync(new() { Timeout = 60_000 });
        (await page.Locator("article.line").CountAsync()).Should().Be(0, "nobody has spoken yet");
        (await page.Locator("div.rows[aria-busy=true]").CountAsync())
            .Should().Be(0, "waiting for a person is not the page loading, and a spinner would say it was");

        var scrollWidth = await page.EvaluateAsync<int>("document.scrollingElement.scrollWidth");
        var innerWidth = await page.EvaluateAsync<int>("window.innerWidth");
        scrollWidth.Should().BeLessThanOrEqualTo(innerWidth, "the play screen must never scroll sideways at 390 px");

        await turn.Locator("textarea[name=line]").FillAsync("We are not having this argument again.");
        await turn.GetByRole(AriaRole.Button, new() { Name = "Say it" }).ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelectorAll('article.line').length >= 2", null, new() { Timeout = 60_000 });
        (await page.Locator("article.line").First.InnerTextAsync()).Should().Contain("not having this argument");

        errors.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task Leaving_mid_argument_stops_it_and_the_screenshots_are_taken_on_the_way_through()
    {
        var (context, page, errors) = await app.OpenAsync(AppFixture.Desktop, "/profiles", user: "e2e-shots");
        await using var _ = context;

        await SeedAsync(page);
        await page.GotoAsync($"{app.BaseUrl}/watch");
        await page.Locator("div.picker").First.WaitForAsync(new() { Timeout = 30_000 });

        await ChooseAsync(page, side: 0, "Matthew");
        await ChooseAsync(page, side: 1, "Kimberly");
        await ShootAsync(page, "watch-setup");

        await page.GetByRole(AriaRole.Button, new() { Name = "Start the argument" }).ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelectorAll('article.line').length >= 2", null, new() { Timeout = 90_000 });
        await ShootAsync(page, "watch-play");

        // Walking out mid-argument must stop it dead rather than leave lines arriving behind the setup screen.
        await page.GetByRole(AriaRole.Button, new() { Name = "Leave" }).ClickAsync();
        await page.Locator("div.picker").First.WaitForAsync(new() { Timeout = 15_000 });
        var settled = await page.Locator("article.line").CountAsync();
        await page.WaitForTimeoutAsync(3_000);
        (await page.Locator("article.line").CountAsync()).Should().Be(settled, "the argument does not carry on once it has been left");

        errors.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task The_ruling_is_shown_and_photographed()
    {
        var (context, page, errors) = await app.OpenAsync(AppFixture.Desktop, "/profiles", user: "e2e-ruling");
        await using var _ = context;

        await SeedAsync(page);
        await page.GotoAsync($"{app.BaseUrl}/watch");
        await page.Locator("div.picker").First.WaitForAsync(new() { Timeout = 30_000 });
        await ChooseAsync(page, side: 0, "Donald");
        await ChooseAsync(page, side: 1, "Hillary");
        await page.GetByRole(AriaRole.Button, new() { Name = "Start the argument" }).ClickAsync();

        var verdict = page.Locator("section.verdict");
        await verdict.WaitForAsync(new() { Timeout = 120_000 });
        (await verdict.InnerTextAsync()).Should().ContainEquivalentOf("won", "the ruling says who took it, and innerText carries the display font uppercase");
        await ShootAsync(page, "watch-verdict");

        // A rematch starts a fresh argument rather than appending to the one just judged.
        await page.GetByRole(AriaRole.Button, new() { Name = "Argue again" }).ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('section.verdict').length === 0 && document.querySelectorAll('article.line').length <= 2",
            null,
            new() { Timeout = 30_000 });

        errors.Should().BeEmpty();
    }

    private static async Task ShootAsync(IPage page, string name)
    {
        Directory.CreateDirectory(ShotsDirectory);

        // Without this the icon font can still be loading and every icon photographs as its ligature name.
        await page.EvaluateAsync("() => document.fonts.ready");
        await page.ScreenshotAsync(new() { Path = Path.Combine(ShotsDirectory, $"{name}.png"), FullPage = true });
    }

    private static async Task SeedAsync(IPage page)
    {
        var load = page.GetByRole(AriaRole.Button, new() { Name = "Load default cast" });
        await load.First.WaitForAsync(new() { Timeout = 30_000 });
        await load.First.ClickAsync();
        var confirm = page.Locator(".rz-dialog");
        await confirm.WaitForAsync(new() { Timeout = 10_000 });
        await confirm.GetByRole(AriaRole.Button, new() { Name = "Load cast" }).ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelectorAll('article.profile').length >= 8", null, new() { Timeout = 30_000 });
    }

    private static async Task ChooseAsync(IPage page, int side, string option)
    {
        await page.Locator("div.picker").Nth(side).Locator(".rz-dropdown").ClickAsync();
        var panel = page.Locator(".rz-dropdown-panel:visible");
        await panel.WaitForAsync(new() { Timeout = 10_000 });
        await panel.Locator(".rz-dropdown-item", new() { HasText = option }).First.ClickAsync();
        await panel.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }
}
