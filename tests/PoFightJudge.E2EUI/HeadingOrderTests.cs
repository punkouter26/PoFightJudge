using Microsoft.Playwright;

namespace PoFightJudge.E2EUI;

/// <summary>
/// The document outline, read the way a screen reader reads it (WCAG 1.3.1). A card that hardcodes its own heading
/// level decides the outline of every page it lands on, which is how /2p came to go h1 → h3 with no h2 between.
/// The cast is loaded first: the cards that carry these headings do not render on an empty account, so a test that
/// skipped that step would have passed on the empty state.
/// </summary>
[Collection(AppCollection.Name)]
public class HeadingOrderTests(AppFixture app)
{
    private static readonly string[] Routes = ["/", "/cpu", "/1p", "/2p", "/profiles", "/fighters", "/history", "/health"];

    [SkippableFact]
    public async Task No_page_skips_a_heading_level()
    {
        var (context, page, errors) = await app.OpenAsync(AppFixture.Desktop, "/profiles", user: "e2e-headings");
        await using var _ = context;

        var load = page.GetByRole(AriaRole.Button, new() { Name = "Load default cast" });
        await load.First.WaitForAsync(new() { Timeout = 30_000 });
        await load.First.ClickAsync();
        var confirm = page.Locator(".rz-dialog");
        await confirm.WaitForAsync(new() { Timeout = 10_000 });
        await confirm.GetByRole(AriaRole.Button, new() { Name = "Load cast" }).ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelectorAll('article.profile').length >= 8", null, new() { Timeout = 30_000 });

        var problems = new List<string>();
        foreach (var route in Routes)
        {
            await page.GotoAsync(app.BaseUrl + route, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });
            await AppFixture.WaitForAppAsync(page, errors);
            await page.Locator("h1").First.WaitForAsync(new() { Timeout = 30_000 });

            var levels = await page.EvaluateAsync<int[]>(
                "() => [...document.querySelectorAll('h1,h2,h3,h4,h5,h6')].map(h => Number(h.tagName[1]))");

            levels.Should().NotBeEmpty($"{route} has a heading");
            levels[0].Should().Be(1, $"{route} opens on its h1");
            for (var i = 1; i < levels.Length; i++)
            {
                if (levels[i] > levels[i - 1] + 1)
                {
                    problems.Add($"{route}: h{levels[i - 1]} → h{levels[i]} (position {i})");
                }
            }
        }

        problems.Should().BeEmpty("a heading level may deepen by one at a time, and skipping one loses the structure");
        errors.Should().BeEmpty();
    }
}
