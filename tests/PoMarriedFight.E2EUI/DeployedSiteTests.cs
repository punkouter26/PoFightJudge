using Microsoft.Playwright;
using PoMarriedFight.Shared;

namespace PoMarriedFight.E2EUI;

/// <summary>
/// Smoke test for a site that is already deployed, driven by <c>POMARRIEDFIGHT_SMOKE_URL</c> (the pipeline sets it
/// after the App Service deploy; locally the test skips). It hosts nothing and shares no fixture with the rest of the
/// suite — the point is to touch the real thing.
///
/// Status codes are not enough on their own: a Blazor WASM app that fails to boot still serves its shell with 200.
/// Both source repositories shipped exactly that at least once — a missing MSAL <c>AuthenticationService.js</c>, and
/// an authority Entra rejected — and each looked healthy to curl while being dead on screen. So the assertions here
/// are what a person would check: the boot placeholder is gone, the sign-in rendered, and the console stayed quiet.
/// </summary>
public class DeployedSiteTests
{
    private static string? SmokeUrl => Environment.GetEnvironmentVariable("POMARRIEDFIGHT_SMOKE_URL")?.TrimEnd('/');

    [SkippableFact]
    public async Task The_deployed_site_boots_and_asks_for_a_sign_in()
    {
        Skip.If(string.IsNullOrWhiteSpace(SmokeUrl), "POMARRIEDFIGHT_SMOKE_URL is not set; this test only runs against a deployment.");

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var context = await browser.NewContextAsync();
        await using var _ = context;
        var page = await context.NewPageAsync();

        var problems = new List<string>();
        page.PageError += (_, e) => problems.Add($"page error: {e}");
        page.Console += (_, e) =>
        {
            if (string.Equals(e.Type, "error", StringComparison.Ordinal))
            {
                problems.Add($"console: {e.Text}");
            }
        };

        await page.GotoAsync(SmokeUrl!, new() { WaitUntil = WaitUntilState.Load, Timeout = 90_000 });

        // Anonymous, so the router has to land on the sign-in page rather than on the two channels.
        await page.WaitForURLAsync("**/login**", new() { Timeout = 60_000 });

        // .po-boot is the "Loading PoMarriedFight…" placeholder in index.html: still there means the runtime never started.
        await page.Locator(".po-boot").WaitForAsync(new() { State = WaitForSelectorState.Detached, Timeout = 60_000 });
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in with Microsoft" }).WaitForAsync(new() { Timeout = 30_000 });

        // Health covers storage (managed identity reached the account) and configuration (Key Vault secrets resolved).
        var health = await context.APIRequest.GetAsync($"{SmokeUrl}{ApiRoutes.Health.Url}");
        health.Status.Should().Be(200, "the deployed site must be ready, not merely answering");

        problems.Should().BeEmpty();
    }
}
