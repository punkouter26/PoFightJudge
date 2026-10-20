using Microsoft.Playwright;

namespace PoFightJudge.E2EUI;

/// <summary>
/// Getting around, on both shapes of screen. The nav used to be one Radzen menu that collapsed itself from
/// JavaScript after measuring the bar, which is why the first paint on a phone was a 269 px top bar.
/// </summary>
[Collection(AppCollection.Name)]
public class NavigationTests(AppFixture app)
{
    /// <summary>Every destination, and the group it sits under in the bar (null when it is on the bar itself).</summary>
    private static readonly (string Label, string Path, string? Under)[] Destinations =
    [
        ("Home", "/", null), ("CPU", "/cpu", "Play"), ("1P", "/1p", "Play"), ("2P", "/2p", "Play"),
        ("Profiles", "/profiles", null), ("Leaderboard", "/fighters", null), ("History", "/history", null),
    ];

    [SkippableFact]
    public async Task The_bar_holds_five_destinations_and_one_settings_button()
    {
        var (context, page, errors) = await app.OpenAsync(AppFixture.Desktop, "/");
        await using var _ = context;

        (await page.Locator("header.topbar nav > ul > li").CountAsync()).Should().Be(5, "the three channels are one Play menu");
        (await page.Locator("header.topbar .session button:visible").CountAsync()).Should().Be(1, "user, sound, volume, theme and sign out are one panel");

        await page.GetByRole(AriaRole.Button, new() { Name = "Settings" }).ClickAsync();
        var panel = page.Locator(".po-settings");
        await panel.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        (await panel.Locator(".po-volume").CountAsync()).Should().Be(1);
        await page.Keyboard.PressAsync("Escape");
        await panel.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
        errors.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task A_desktop_reaches_every_destination_from_the_bar()
    {
        var (context, page, errors) = await app.OpenAsync(AppFixture.Desktop, "/");
        await using var _ = context;

        foreach (var (label, path, under) in Destinations)
        {
            var nav = page.Locator("header.topbar nav");
            if (under is not null)
            {
                await nav.GetByText(under, new() { Exact = true }).ClickAsync();
            }

            await nav.GetByRole(AriaRole.Link, new() { Name = label, Exact = true }).ClickAsync();
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

        foreach (var (label, path, _) in Destinations)
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
