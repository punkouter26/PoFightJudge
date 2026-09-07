using Microsoft.Playwright;

namespace PoMarriedFight.E2EUI;

[Collection(AppCollection.Name)]
public class WatchFlowTests(AppFixture app)
{
    [SkippableFact]
    public async Task Two_personas_argue_from_the_setup_screen_through_to_the_ruling()
    {
        var (context, page, errors) = await app.OpenAsync(AppFixture.Desktop, "/profiles", user: "e2e-watcher");
        await using var _ = context;

        await LoadTheCastAsync(page);

        await page.GotoAsync($"{app.BaseUrl}/cpu");
        await page.Locator("div.picker").First.WaitForAsync(new() { Timeout = 30_000 });

        await ChooseAsync(page, side: 0, "Matthew");
        await ChooseAsync(page, side: 1, "Kimberly");

        await page.GetByRole(AriaRole.Button, new() { Name = "Start the argument" }).ClickAsync();

        // Six lines, spoken one after another, then the judge.
        await page.WaitForFunctionAsync("() => document.querySelectorAll('article.line').length >= 6", null, new() { Timeout = 90_000 });
        var verdict = page.Locator("section.verdict");
        await verdict.WaitForAsync(new() { Timeout = 60_000 });

        (await verdict.InnerTextAsync()).Should().NotBeNullOrWhiteSpace();
        (await page.Locator("article.line").CountAsync()).Should().Be(6, "three rounds each, no slap");

        errors.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task A_person_takes_a_side_and_the_argument_waits_for_them()
    {
        var (context, page, errors) = await app.OpenAsync(AppFixture.Desktop, "/profiles", user: "e2e-arguer");
        await using var _ = context;

        await LoadTheCastAsync(page);

        await page.GotoAsync($"{app.BaseUrl}/1p");
        await page.Locator("div.picker").First.WaitForAsync(new() { Timeout = 30_000 });

        // 1P asks one thing: who you are arguing with. Matthew is a husband, so the wife's seat is yours.
        await ChooseAsync(page, side: 0, "Matthew");
        await page.Locator("input[name=yourTag]").FillAsync("KD");

        await page.GetByRole(AriaRole.Button, new() { Name = "Start the argument" }).ClickAsync();

        var turn = page.Locator("section.your-turn");
        await turn.WaitForAsync(new() { Timeout = 90_000 });
        (await page.Locator("article.line").CountAsync()).Should().Be(1, "the argument stops at the person's turn rather than speaking for them");

        // Spoken, not typed: the fake device plays the fixture, the clip goes to the transcriber, and what comes
        // back lands in the box for the person to check before it counts.
        await turn.GetByRole(AriaRole.Button, new() { Name = "Speak it" }).ClickAsync();
        await turn.Locator(".rec").WaitForAsync(new() { Timeout = 10_000 });
        await page.WaitForTimeoutAsync(1_500);
        await turn.GetByRole(AriaRole.Button, new() { Name = "Stop and listen back" }).ClickAsync();

        var line = turn.Locator("textarea[name=line]");
        await page.WaitForFunctionAsync(
            "() => (document.querySelector('textarea[name=line]')?.value ?? '').length > 0",
            null,
            new() { Timeout = 60_000 });
        (await line.InputValueAsync()).Should().Contain("fake transcript", "the offline transcriber says so out loud");

        await turn.GetByRole(AriaRole.Button, new() { Name = "Say it" }).ClickAsync();

        await page.WaitForFunctionAsync("() => document.querySelectorAll('article.line').length >= 3", null, new() { Timeout = 60_000 });
        (await page.Locator("article.line").Nth(1).InnerTextAsync()).Should().Contain("fake transcript", "what they said is what went on the record");

        errors.Should().BeEmpty();
    }

    private static async Task LoadTheCastAsync(IPage page)
    {
        var load = page.GetByRole(AriaRole.Button, new() { Name = "Load default cast" });
        await load.First.WaitForAsync(new() { Timeout = 30_000 });
        await load.First.ClickAsync();
        var confirm = page.Locator(".rz-dialog");
        await confirm.WaitForAsync(new() { Timeout = 10_000 });
        await confirm.GetByRole(AriaRole.Button, new() { Name = "Load cast" }).ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelectorAll('article.profile').length >= 8", null, new() { Timeout = 30_000 });
    }

    /// <summary>Picks an option out of one side's dropdown; side 0 is the husband, side 1 the wife.</summary>
    private static async Task ChooseAsync(IPage page, int side, string option)
    {
        await page.Locator("div.picker").Nth(side).Locator(".rz-dropdown").ClickAsync();
        var panel = page.Locator(".rz-dropdown-panel:visible");
        await panel.WaitForAsync(new() { Timeout = 10_000 });
        await panel.Locator(".rz-dropdown-item", new() { HasText = option }).First.ClickAsync();
        await panel.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }
}
