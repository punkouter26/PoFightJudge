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
        (await panel.Locator(".host-list").CountAsync()).Should().Be(1, "who hosts is a setting, not a setup question");
        (await panel.Locator(".rz-selectbar:has-text('Spicy')").CountAsync()).Should().Be(1, "so is how hot a spin runs");
        await page.Keyboard.PressAsync("Escape");
        await panel.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });

        await page.GotoAsync(app.BaseUrl + "/2p");
        await page.GetByRole(AriaRole.Button, new() { Name = "Spin" }).WaitForAsync(new() { Timeout = 15_000 });
        (await page.Locator("main .host-list, main .rz-selectbar:has-text('Spicy')").CountAsync()).Should().Be(0, "setup asks only what this fight needs");
        errors.Should().BeEmpty();
    }

    /// <summary>Every destination, from the bar on a desktop and through the drawer on a phone.</summary>
    [SkippableFact]
    public async Task Every_destination_is_reached_from_the_bar_and_from_the_drawer()
    {
        var (context, page, errors) = await app.OpenAsync(AppFixture.Desktop, "/");
        await using (context)
        {
            foreach (var (label, path, under) in Destinations)
            {
                var nav = page.Locator("header.topbar nav");
                if (under is not null)
                {
                    await nav.GetByText(under, new() { Exact = true }).ClickAsync();
                }

                await nav.GetByRole(AriaRole.Link, new() { Name = label, Exact = true }).ClickAsync();
                await page.WaitForFunctionAsync($"() => location.pathname === '{path}'", null, new() { Timeout = 15_000 });

                // Home is "", and every path starts with "": matched as a prefix it lit up on every page.
                var homeLit = await nav.GetByRole(AriaRole.Link, new() { Name = "Home", Exact = true })
                    .EvaluateAsync<bool>("a => a.className.includes('active') || !!a.closest('.rz-navigation-item-wrapper-active')");
                homeLit.Should().Be(string.Equals(path, "/", StringComparison.Ordinal), $"Home is the current page only at /, not at {path}");
            }

            errors.Should().BeEmpty();
        }

        var (phoneContext, phonePage, phoneErrors) = await app.OpenAsync(AppFixture.Mobile, "/");
        await using var _ = phoneContext;

        var toggle = phonePage.GetByRole(AriaRole.Button, new() { Name = "Menu" });
        await toggle.WaitForAsync(new() { Timeout = 30_000 });

        foreach (var (label, path, _) in Destinations)
        {
            await toggle.ClickAsync();
            var drawer = phonePage.Locator(".po-drawer");
            await drawer.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
            await drawer.GetByRole(AriaRole.Link, new() { Name = label, Exact = true }).ClickAsync();
            await phonePage.WaitForFunctionAsync($"() => location.pathname === '{path}'", null, new() { Timeout = 15_000 });

            // Choosing a destination closes the drawer; a drawer left open over the page it opened is a dead end.
            await drawer.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
        }

        phoneErrors.Should().BeEmpty();
    }
}
