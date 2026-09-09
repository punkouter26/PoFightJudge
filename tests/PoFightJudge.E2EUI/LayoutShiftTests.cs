using Microsoft.Playwright;

namespace PoFightJudge.E2EUI;

/// <summary>
/// Cumulative Layout Shift, read from the browser's own layout-shift entries rather than eyeballed. The banner
/// strip above the top bar renders nothing until the feature flags come back from the API, so every page load used
/// to settle, then push the whole app down by the height of a banner.
/// </summary>
[Collection(AppCollection.Name)]
public class LayoutShiftTests(AppFixture app)
{
    /// <summary>Web Vitals calls 0.1 "good". Nothing here has a reason to shift at all.</summary>
    private const double Budget = 0.1;

    private static readonly string[] Routes = ["/", "/cpu", "/2p", "/history"];

    [SkippableFact]
    public async Task No_route_shifts_its_layout_after_first_paint()
    {
        var noisy = new List<string>();
        foreach (var route in Routes)
        {
            var (context, page, errors) = await app.OpenAsync(AppFixture.Mobile, route);
            await using var _ = context;

            // Installed for the *next* navigation: an observer added after load has already missed the shifts.
            await context.AddInitScriptAsync(@"
                window.__cls = 0;
                new PerformanceObserver(list => {
                  for (const entry of list.getEntries()) {
                    if (!entry.hadRecentInput) { window.__cls += entry.value; }
                  }
                }).observe({ type: 'layout-shift', buffered: true });");

            await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });
            await AppFixture.WaitForAppAsync(page, errors);
            // Long enough for the flags call, and for anything it turns on, to have landed.
            await page.WaitForTimeoutAsync(1500);

            var cls = await page.EvaluateAsync<double>("() => window.__cls ?? 0");
            if (cls > Budget)
            {
                noisy.Add($"{route}: CLS {cls:F3}");
            }
        }

        noisy.Should().BeEmpty("a banner that appears after the API answers pushes the whole page down");
    }
}
