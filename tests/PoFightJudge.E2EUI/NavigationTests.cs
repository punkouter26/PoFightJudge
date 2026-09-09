using Microsoft.Playwright;

namespace PoFightJudge.E2EUI;

/// <summary>
/// Getting around, on both shapes of screen. The nav used to be one Radzen menu that collapsed itself from
/// JavaScript after measuring the bar, which is why the first paint on a phone was a 269 px top bar.
/// </summary>
[Collection(AppCollection.Name)]
public class NavigationTests(AppFixture app)
{
    private static readonly (string Label, string Path)[] Destinations =
    [
        ("Home", "/"), ("CPU", "/cpu"), ("1P", "/1p"), ("2P", "/2p"),
        ("Profiles", "/profiles"), ("Fighters", "/fighters"), ("History", "/history"),
    ];

    [SkippableFact]
    public async Task A_desktop_reaches_every_destination_from_the_bar()
    {
        var (context, page, errors) = await app.OpenAsync(AppFixture.Desktop, "/");
        await using var _ = context;

        foreach (var (label, path) in Destinations)
        {
            await page.Locator("header.topbar nav").GetByRole(AriaRole.Link, new() { Name = label, Exact = true }).ClickAsync();
            await page.WaitForFunctionAsync($"() => location.pathname === '{path}'", null, new() { Timeout = 15_000 });
        }

        errors.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task A_phone_reaches_every_destination_through_the_drawer()
    {
        var (context, page, errors) = await app.OpenAsync(AppFixture.Mobile, "/");
        await using var _ = context;

        var toggle = page.GetByRole(AriaRole.Button, new() { Name = "Menu" });
        await toggle.WaitForAsync(new() { Timeout = 30_000 });

        foreach (var (label, path) in Destinations)
        {
            await toggle.ClickAsync();
            var drawer = page.Locator(".po-drawer");
            await drawer.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
            await drawer.GetByRole(AriaRole.Link, new() { Name = label, Exact = true }).ClickAsync();
            await page.WaitForFunctionAsync($"() => location.pathname === '{path}'", null, new() { Timeout = 15_000 });

            // Choosing a destination closes the drawer; a drawer left open over the page it opened is a dead end.
            await drawer.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
        }

        errors.Should().BeEmpty();
    }
}
