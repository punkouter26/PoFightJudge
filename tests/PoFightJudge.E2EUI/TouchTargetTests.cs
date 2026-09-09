using Microsoft.Playwright;

namespace PoFightJudge.E2EUI;

/// <summary>
/// Hit boxes, measured on a phone rather than asserted in a stylesheet. app.css has claimed a 44 px minimum since
/// the design system went in, and it was losing: Radzen sizes its buttons from <c>--rz-button-size-*</c> in a sheet
/// <c>&lt;RadzenTheme&gt;</c> injects after app.css, at the same specificity, so the later rule won and the session
/// buttons measured 36×36 with the menu toggle at 24×24.
/// </summary>
[Collection(AppCollection.Name)]
public class TouchTargetTests(AppFixture app)
{
    /// <summary>WCAG 2.2 SC 2.5.5 (AAA), which is the size app.css asks for. The persistent chrome is held to it.</summary>
    private const int Comfortable = 44;

    /// <summary>WCAG 2.2 SC 2.5.8 (AA). Everything that can be pressed clears at least this.</summary>
    private const int Minimum = 24;

    private static readonly string[] Routes = ["/", "/cpu", "/2p", "/profiles", "/fighters", "/history", "/leaderboard"];

    [SkippableFact]
    public async Task The_persistent_chrome_is_44px_on_a_phone()
    {
        var (context, page, errors) = await app.OpenAsync(AppFixture.Mobile, "/");
        await using var _ = context;

        var small = await MeasureAsync(page, "header.topbar button, header.topbar .rz-menu-toggle", Comfortable);
        small.Should().BeEmpty("the top bar is the one set of controls that is on every screen");
        errors.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task Nothing_pressable_is_under_the_minimum_target_size()
    {
        var offenders = new List<string>();
        foreach (var route in Routes)
        {
            var (context, page, _) = await app.OpenAsync(AppFixture.Mobile, route);
            await using var _ = context;
            offenders.AddRange((await MeasureAsync(page, "button, a[href], [role=button]", Minimum)).Select(o => $"{route} {o}"));
        }

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
}
