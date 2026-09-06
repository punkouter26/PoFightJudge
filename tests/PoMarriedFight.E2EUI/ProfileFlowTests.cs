using Microsoft.Playwright;

namespace PoMarriedFight.E2EUI;

[Collection(AppCollection.Name)]
public class ProfileFlowTests(AppFixture app)
{
    [SkippableFact]
    public async Task Create_a_profile_in_the_dialog_see_it_on_the_cast_then_delete_it()
    {
        var (context, page, errors) = await app.OpenAsync(AppFixture.Desktop, "/profiles");
        await using var _ = context;

        var newProfile = page.GetByRole(AriaRole.Button, new() { Name = "New profile" });
        await newProfile.First.WaitForAsync(new() { Timeout = 30_000 });
        await newProfile.First.ClickAsync();

        var dialog = page.Locator(".rz-dialog");
        await dialog.WaitForAsync(new() { Timeout = 15_000 });

        // Validation first: an empty form must not leave the dialog.
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Create profile" }).ClickAsync();
        await dialog.Locator(".validation-message", new() { HasText = "Initials are required." }).WaitForAsync(new() { Timeout = 10_000 });

        await dialog.Locator("input[name=Initials]").FillAsync("zzq");
        await dialog.Locator("input[name=Name]").FillAsync("Zed Quill");
        await dialog.Locator("textarea[name=Likes]").FillAsync("quiet mornings");
        await dialog.Locator("textarea[name=Dislikes]").FillAsync("loud chewing");
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Create profile" }).ClickAsync();

        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
        var card = page.Locator("article.profile", new() { HasText = "ZZQ" });
        await card.WaitForAsync(new() { Timeout = 15_000 });
        (await card.InnerTextAsync()).Should().ContainEquivalentOf("Zed Quill", "innerText reflects the display font uppercase transform");

        await card.GetByRole(AriaRole.Button, new() { Name = "Delete ZZQ" }).ClickAsync();
        var confirm = page.Locator(".rz-dialog");
        await confirm.WaitForAsync(new() { Timeout = 10_000 });
        await confirm.GetByRole(AriaRole.Button, new() { Name = "Delete" }).ClickAsync();
        await card.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

        errors.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task Loading_the_default_cast_fills_both_role_sections_with_portraits()
    {
        var (context, page, errors) = await app.OpenAsync(AppFixture.Desktop, "/profiles", user: "e2e-seeder");
        await using var _ = context;

        var load = page.GetByRole(AriaRole.Button, new() { Name = "Load default cast" });
        await load.First.WaitForAsync(new() { Timeout = 30_000 });
        await load.First.ClickAsync();
        var confirm = page.Locator(".rz-dialog");
        await confirm.WaitForAsync(new() { Timeout = 10_000 });
        await confirm.GetByRole(AriaRole.Button, new() { Name = "Load cast" }).ClickAsync();

        var cards = page.Locator("article.profile");
        await page.WaitForFunctionAsync("() => document.querySelectorAll('article.profile').length >= 8", null, new() { Timeout = 30_000 });
        (await page.Locator("section.cast").CountAsync()).Should().Be(2, "husbands and wives are separate sections");
        var mah = page.Locator("article.profile", new() { HasText = "MAH" });
        await mah.Locator("img.face").WaitForAsync(new() { Timeout = 15_000 });
        (await mah.Locator("img.face").EvaluateAsync<bool>("img => img.complete && img.naturalWidth === 512")).Should().BeTrue("the seeded portrait is served as the 512 px PNG");

        errors.Should().BeEmpty();
    }
}
