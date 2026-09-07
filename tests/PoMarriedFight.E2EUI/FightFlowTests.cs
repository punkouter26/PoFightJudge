using Microsoft.Playwright;

namespace PoMarriedFight.E2EUI;

/// <summary>
/// A whole fight in a real browser: two tags, a microphone playing the fixture, the scripted host running the show
/// from those frames, and the ruling at the end. No key, no network beyond the app itself.
/// </summary>
[Collection(AppCollection.Name)]
public class FightFlowTests(AppFixture app)
{
    private static readonly string ShotsDirectory = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "screenshots");

    [SkippableFact]
    public async Task Two_people_argue_in_front_of_the_host_and_it_rules_at_the_end()
    {
        var (context, page, errors) = await app.OpenAsync(AppFixture.Desktop, "/fight", user: "e2e-fighter");
        await using var _ = context;

        await TagBox(page, "player1Tag").WaitForAsync(new() { Timeout = 30_000 });
        await TypeTagAsync(page, "player1Tag", "AL");
        await TypeTagAsync(page, "player2Tag", "SM");
        await page.Locator("input[name=topic]").FillAsync("who does the dishes");
        await ShootAsync(page, "fight-setup");

        await page.GetByRole(AriaRole.Button, new() { Name = "Start the fight" }).ClickAsync();

        // The fight page owns the microphone from here; the fake device plays the fixture into it.
        await page.WaitForURLAsync(u => u.Contains("/fight/", StringComparison.Ordinal), new() { Timeout = 30_000 });
        // The scoreboard renders before the first snapshot lands, so wait for the tags rather than for the section.
        await page.Locator("section.scoreboard .side[data-tag=AL]").WaitForAsync(new() { Timeout = 60_000 });
        await page.Locator("section.scoreboard .side[data-tag=SM]").WaitForAsync(new() { Timeout = 30_000 });

        await page.Locator(".mic--on").WaitForAsync(new() { Timeout = 30_000 });

        // The scripted host advances on microphone frames, so captions are proof the audio path works end to end.
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('article.caption').length > 0",
            null,
            new() { Timeout = 90_000 });
        await page.WaitForFunctionAsync(
            "() => (document.querySelector('.phase')?.getAttribute('data-phase') ?? 'intro') !== 'intro'",
            null,
            new() { Timeout = 90_000 });
        await ShootAsync(page, "fight-live");

        // The show runs itself to the ruling and hands over to the verdict.
        await page.WaitForURLAsync(u => u.Contains("/verdict/", StringComparison.Ordinal), new() { Timeout = 180_000 });

        errors.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task A_fight_can_be_stopped_by_the_room_and_the_microphone_closes_with_it()
    {
        var (context, page, errors) = await app.OpenAsync(AppFixture.Desktop, "/fight", user: "e2e-stopper");
        await using var _ = context;

        await TagBox(page, "player1Tag").WaitForAsync(new() { Timeout = 30_000 });
        await TypeTagAsync(page, "player1Tag", "ZA");
        await TypeTagAsync(page, "player2Tag", "ZB");
        await page.GetByRole(AriaRole.Button, new() { Name = "Start the fight" }).ClickAsync();

        await page.Locator(".mic--on").WaitForAsync(new() { Timeout = 60_000 });
        await page.GetByRole(AriaRole.Button, new() { Name = "Stop the fight" }).ClickAsync();

        await page.WaitForURLAsync(u => u.Contains("/verdict/", StringComparison.Ordinal), new() { Timeout = 60_000 });
        errors.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task A_fight_that_never_existed_says_so_instead_of_opening_the_microphone()
    {
        var (context, page, errors) = await app.OpenAsync(AppFixture.Desktop, "/fight/00000000000000000000000000000000", user: "e2e-ghost");
        await using var _ = context;

        var problem = page.Locator(".problem");
        await problem.WaitForAsync(new() { Timeout = 30_000 });
        (await problem.InnerTextAsync()).Should().ContainEquivalentOf("could not be joined");
        (await page.Locator(".mic--on").CountAsync()).Should().Be(0, "there is nothing to record into");

        errors.Should().BeEmpty();
    }

    /// <summary>The autocomplete renders its own input, so the wrapper is what carries a stable name.</summary>
    private static ILocator TagBox(IPage page, string name) => page.Locator($"div.tag-input[data-name={name}] input");

    /// <summary>Types a tag and moves on, so the value is published however the control reports it.</summary>
    private static async Task TypeTagAsync(IPage page, string name, string tag)
    {
        var input = TagBox(page, name);
        await input.FillAsync(tag);
        await input.PressAsync("Tab", new() { Delay = 20 });
    }

    private static async Task ShootAsync(IPage page, string name)
    {
        Directory.CreateDirectory(ShotsDirectory);
        await page.EvaluateAsync("() => document.fonts.ready");
        await page.ScreenshotAsync(new() { Path = Path.Combine(ShotsDirectory, $"{name}.png"), FullPage = true });
    }
}
