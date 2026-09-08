using Microsoft.Playwright;

namespace PoFightJudge.E2EUI;

[Collection(AppCollection.Name)]
public class ThemeAndViewportTests(AppFixture app)
{
    [SkippableFact]
    public async Task Unauthenticated_visitor_is_sent_to_login_and_guest_returns_them_home()
    {
        var (context, page, errors) = await app.OpenAsync(AppFixture.Desktop, "/", user: null);
        await using var _ = context;

        var guest = page.GetByRole(AriaRole.Button, new() { Name = "Continue as Guest (Dev)" });
        await guest.WaitForAsync(new() { Timeout = 30_000 });
        page.Url.Should().EndWith("/login?returnUrl=%2F", "the protected home page redirects and remembers where to return");
        await guest.ClickAsync();

        await page.Locator("article.channel").First.WaitForAsync(new() { Timeout = 30_000 });
        page.Url.Should().Be(app.BaseUrl + "/");
        (await page.Locator("article.channel").CountAsync()).Should().Be(3, "CPU, 1P and 2P");
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
