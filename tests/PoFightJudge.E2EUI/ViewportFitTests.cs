using Microsoft.Playwright;

namespace PoFightJudge.E2EUI;

/// <summary>
/// Whether a screen fits the screen. Measured on a phone held upright, which is the hard case: /2p was 1755 px of
/// page in an 844 px viewport, and the home page 1435.
///
/// Fitting means fitting, not truncating — the tests below check the page is inside the viewport *and* that what it
/// was showing is still reachable.
///
/// The same walk measures hit boxes, on the phone rather than in a stylesheet. app.css has claimed a 44 px minimum
/// since the design system went in, and it was losing: Radzen sizes its buttons from <c>--rz-button-size-*</c> in a
/// sheet <c>&lt;RadzenTheme&gt;</c> injects after app.css, at the same specificity, so the later rule won and the
/// session buttons measured 36×36 with the menu toggle at 24×24.
/// </summary>
[Collection(AppCollection.Name)]
public class ViewportFitTests(AppFixture app)
{
    /// <summary>A page may run over by this much before it counts as scrolling. One line of text, roughly.</summary>
    private const int Slack = 24;

    /// <summary>WCAG 2.2 SC 2.5.8 (AA). Everything that can be pressed clears at least this.</summary>
    private const int Minimum = 24;

    private static readonly string[] SetupRoutes = ["/", "/cpu", "/1p", "/2p", "/login"];

    private static readonly string[] PressRoutes = ["/", "/cpu", "/2p", "/profiles", "/fighters", "/history"];

    [SkippableFact]
    public async Task Every_setup_screen_fits_a_phone_and_nothing_pressable_is_too_small()
    {
        // Its own cast. An empty account renders an empty state on both watch channels, which fits anything — the
        // first version of this test passed for that reason and only failed once a neighbour had seeded one.
        await LoadTheCastAsync();

        var over = new List<string>();
        var offenders = new List<string>();
        foreach (var route in SetupRoutes.Union(PressRoutes, StringComparer.Ordinal))
        {
            var (context, page, _) = await app.OpenAsync(AppFixture.Mobile, route);
            await using var _ = context;
            await page.WaitForTimeoutAsync(1200);

            if (SetupRoutes.Contains(route, StringComparer.Ordinal))
            {
                var height = await page.EvaluateAsync<int>("() => document.scrollingElement.scrollHeight");
                var viewport = await page.EvaluateAsync<int>("() => window.innerHeight");
                if (height > viewport + Slack)
                {
                    over.Add($"{route}: {height}px in {viewport}px — {height - viewport}px over");
                }
            }

            if (PressRoutes.Contains(route, StringComparer.Ordinal))
            {
                offenders.AddRange((await MeasureAsync(page, "button, a[href], [role=button]", Minimum)).Select(o => $"{route} {o}"));
            }
        }

        over.Should().BeEmpty("a phone should not have to scroll to reach the button that starts the argument");
        offenders.Should().BeEmpty("WCAG 2.2 SC 2.5.8 puts the floor at 24 x 24 CSS px");
    }

    /// <summary>
    /// Every visible match under <paramref name="floor"/> in either direction. The brand wordmark is excluded: it is
    /// a heading that happens to link home, and the rule is about controls.
    /// </summary>
    private static async Task<List<string>> MeasureAsync(IPage page, string selector, int floor)
    {
        var found = await page.EvaluateAsync<string[]>(
            @"([selector, floor]) => [...document.querySelectorAll(selector)]
                .filter(e => !e.closest('.brand'))
                .map(e => ({ e, r: e.getBoundingClientRect() }))
                .filter(x => x.r.width > 0 && x.r.height > 0 && (x.r.width < floor || x.r.height < floor))
                .map(x => (x.e.innerText || x.e.getAttribute('aria-label') || x.e.className || '?')
                    .trim().split('\n')[0].slice(0, 24)
                    + ' ' + Math.round(x.r.width) + 'x' + Math.round(x.r.height))",
            new object[] { selector, floor });
        return [.. found];
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
