using Microsoft.Playwright;

namespace PoMarriedFight.E2EUI;

[Collection(AppCollection.Name)]
public class ThemeAndViewportTests(AppFixture app)
{
    [SkippableFact]
    public async Task Guest_sign_in_authenticates_the_session()
    {
        var (context, page, errors) = await app.OpenAsync(AppFixture.Desktop, "/login", user: null);
        await using var _ = context;

        var guest = page.GetByRole(AriaRole.Button, new() { Name = "Continue as Guest (Dev)" });
        await guest.WaitForAsync(new() { Timeout = 30_000 });
        await guest.ClickAsync();

        // Home does not exist yet (T10), but the session does: the layout shows the signed-in user.
        await page.Locator("header.topbar .user").WaitForAsync(new() { Timeout = 30_000 });
        (await page.Locator("header.topbar .user").InnerTextAsync()).Should().Contain("GUEST-");
        errors.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task Theme_toggle_persists_across_reload()
    {
        var (context, page, errors) = await app.OpenAsync(AppFixture.Desktop, "/login");
        await using var _ = context;

        (await page.EvaluateAsync<string?>("document.documentElement.getAttribute('data-theme')")).Should().Be("dark", "dark is the default");

        await page.GetByRole(AriaRole.Button, new() { Name = "Switch to light theme" }).ClickAsync();
        await page.WaitForFunctionAsync("() => document.documentElement.getAttribute('data-theme') === 'light'");

        await page.ReloadAsync(new() { WaitUntil = WaitUntilState.NetworkIdle });
        await AppFixture.WaitForAppAsync(page, errors);
        (await page.EvaluateAsync<string?>("document.documentElement.getAttribute('data-theme')")).Should().Be("light");
        (await page.EvaluateAsync<string?>("localStorage.getItem('po-theme')")).Should().Be("light");
        errors.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task Layout_has_no_horizontal_scroll_on_a_phone()
    {
        var (context, page, errors) = await app.OpenAsync(AppFixture.Mobile, "/login", user: null);
        await using var _ = context;

        await page.GetByRole(AriaRole.Button, new() { Name = "Continue as Guest (Dev)" }).WaitForAsync(new() { Timeout = 30_000 });
        var scrollWidth = await page.EvaluateAsync<int>("document.scrollingElement.scrollWidth");
        var innerWidth = await page.EvaluateAsync<int>("window.innerWidth");

        scrollWidth.Should().BeLessThanOrEqualTo(innerWidth, "the page body must never scroll horizontally at 390 px");
        errors.Should().BeEmpty();
    }
}
