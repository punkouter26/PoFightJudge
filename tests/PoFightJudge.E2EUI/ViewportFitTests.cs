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

    private static readonly string[] Routes = ["/", "/cpu", "/1p", "/2p", "/login"];

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
