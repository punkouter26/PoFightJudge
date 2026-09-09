using Microsoft.Playwright;

namespace PoFightJudge.E2EUI;

/// <summary>
/// Whether a screen fits the screen. Measured on a phone held upright, which is the hard case: /2p was 1755 px of
/// page in an 844 px viewport, and the home page 1435.
///
/// Fitting means fitting, not truncating — the tests below check the page is inside the viewport *and* that what it
/// was showing is still reachable.
/// </summary>
[Collection(AppCollection.Name)]
public class ViewportFitTests(AppFixture app)
{
    /// <summary>A page may run over by this much before it counts as scrolling. One line of text, roughly.</summary>
    private const int Slack = 24;

    private static readonly string[] Routes = ["/", "/cpu", "/1p", "/2p", "/login", "/leaderboard"];

    [SkippableFact]
    public async Task Every_setup_screen_fits_a_phone_held_upright()
    {
        // Its own cast. An empty account renders an empty state on both watch channels, which fits anything — the
        // first version of this test passed for that reason and only failed once a neighbour had seeded one.
        await LoadTheCastAsync();

        var over = new List<string>();
        foreach (var route in Routes)
        {
            var (context, page, _) = await app.OpenAsync(AppFixture.Mobile, route);
            await using var _ = context;
            await page.WaitForTimeoutAsync(1200);

            var height = await page.EvaluateAsync<int>("() => document.scrollingElement.scrollHeight");
            var viewport = await page.EvaluateAsync<int>("() => window.innerHeight");
            if (height > viewport + Slack)
            {
                over.Add($"{route}: {height}px in {viewport}px — {height - viewport}px over");
            }
        }

        over.Should().BeEmpty("a phone should not have to scroll to reach the button that starts the argument");
    }

    [SkippableFact]
    public async Task Nothing_the_two_player_setup_asks_for_is_lost_on_a_phone()
    {
        var (context, page, errors) = await app.OpenAsync(AppFixture.Mobile, "/2p");
        await using var _ = context;

        // Every question the one-column version asked is still asked, one step at a time.
        await TagBox(page, "player1Tag").WaitForAsync(new() { Timeout = 30_000 });
        await TypeTagAsync(page, "player1Tag", "AB");
        await TypeTagAsync(page, "player2Tag", "CD");
        (await page.Locator("article.fighter").CountAsync()).Should().Be(2, "both fighters are still previewed");

        await page.Locator("button.rz-steps-next").ClickAsync();
        await page.Locator("input[name=topic]").WaitForAsync(new() { Timeout = 15_000 });
        await page.Locator("input[name=topic]").FillAsync("who does the dishes");
        (await page.Locator("div.hosts").CountAsync()).Should().BeGreaterThan(0, "the host is still chosen");

        await page.Locator("button.rz-steps-next").ClickAsync();

        // The sound check is the last thing before the bell, which is where it has to be.
        await page.GetByRole(AriaRole.Button, new() { Name = "Start the fight" }).WaitForAsync(new() { Timeout = 15_000 });
        (await page.Locator("section.check").CountAsync()).Should().BeGreaterThan(0, "and it is on the same step as the button it comes before");

        // Back out to the first step: nothing typed is lost by moving between them.
        await page.Locator("button.rz-steps-prev").ClickAsync();
        await page.Locator("button.rz-steps-prev").ClickAsync();
        (await TagBox(page, "player1Tag").InputValueAsync()).Should().Be("AB");
        errors.Should().BeEmpty();
    }

    private async Task LoadTheCastAsync()
    {
        var (context, page, _) = await app.OpenAsync(AppFixture.Desktop, "/profiles");
        await using var _ = context;
        var load = page.GetByRole(AriaRole.Button, new() { Name = "Load default cast" });
        await load.First.WaitForAsync(new() { Timeout = 30_000 });
        await load.First.ClickAsync();
        var confirm = page.Locator(".rz-dialog");
        await confirm.WaitForAsync(new() { Timeout = 10_000 });
        await confirm.GetByRole(AriaRole.Button, new() { Name = "Load cast" }).ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelectorAll('article.profile').length >= 8", null, new() { Timeout = 30_000 });
    }

    /// <summary>The autocomplete renders its own input, so the wrapper is what carries a stable name.</summary>
    private static ILocator TagBox(IPage page, string name) => page.Locator($"div.tag-input[data-name={name}] input");

    private static async Task TypeTagAsync(IPage page, string name, string tag)
    {
        var input = TagBox(page, name);
        await input.FillAsync(tag);
        await input.PressAsync("Tab", new() { Delay = 20 });
    }
}
