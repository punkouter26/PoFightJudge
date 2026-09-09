using Microsoft.Playwright;

namespace PoFightJudge.E2EUI;

/// <summary>
/// A whole fight in a real browser: two tags, a microphone playing the fixture, the scripted host running the show
/// from those frames, and the ruling at the end. No key, no network beyond the app itself.
/// </summary>
[Collection(AppCollection.Name)]
public class FightFlowTests(AppFixture app)
{
    private static readonly string ShotsDirectory = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "screenshots");

    /// <summary>
    /// How long the show is given to reach its ruling. The scripted host runs the whole thing in well under a
    /// minute; a real one is paced for people — the debate phase alone may last three minutes by design (SPEC §12
    /// #6), and the probe and the verdict follow it. Three minutes is the fake's budget, not the show's.
    /// </summary>
    private static int RulingTimeout => AppFixture.RealMode ? 480_000 : 180_000;

    [SkippableFact]
    public async Task Two_people_argue_in_front_of_the_host_and_it_rules_at_the_end()
    {
        var (context, page, errors) = await app.OpenAsync(AppFixture.Desktop, "/2p", user: "e2e-fighter");
        await using var _ = context;

        await TagBox(page, "player1Tag").WaitForAsync(new() { Timeout = 30_000 });
        await TypeTagAsync(page, "player1Tag", "AL");
        await TypeTagAsync(page, "player2Tag", "SM");
        await ShootAsync(page, "fight-setup");

        // The topic is the second of the three questions the setup asks.
        await page.Locator("button.rz-steps-next").ClickAsync();
        await page.Locator("input[name=topic]").FillAsync("who does the dishes");

        await AppFixture.ToTheLastStepAsync(page);
        await page.GetByRole(AriaRole.Button, new() { Name = "Start the fight" }).ClickAsync();

        // The fight page owns the microphone from here; the fake device plays the fixture into it.
        await page.WaitForURLAsync(u => u.Contains("/fight/", StringComparison.Ordinal), new() { Timeout = 30_000 });
        // The scoreboard renders before the first snapshot lands, so wait for the tags rather than for the section.
        await page.Locator("section.scoreboard .side[data-tag=AL]").WaitForAsync(new() { Timeout = 60_000 });
        await page.Locator("section.scoreboard .side[data-tag=SM]").WaitForAsync(new() { Timeout = 30_000 });

        await page.Locator(".meter--on").WaitForAsync(new() { Timeout = 30_000 });

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
        await page.WaitForURLAsync(u => u.Contains("/verdict/", StringComparison.Ordinal), new() { Timeout = RulingTimeout });

        // With no key the fight is read by the offline stand-ins; with one, this is the real analysis pipeline.
        await page.Locator("section.ruling").WaitForAsync(new() { Timeout = RulingTimeout });
        (await page.Locator("section.ruling").InnerTextAsync()).Should().ContainEquivalentOf("took it");
        (await page.Locator("article.player").CountAsync()).Should().Be(2, "both of them are reported on");
        (await page.Locator(".trait").CountAsync()).Should().Be(20, "ten judged traits each");
        (await page.Locator(".stat").CountAsync()).Should().BeGreaterThan(40, "every measured number reaches the page");
        (await page.Locator(".tips li").CountAsync()).Should().Be(6, "three pieces of advice each");
        await ShootAsync(page, "verdict");

        // The card writes itself from the fight: both tags are on it, each with the one debate they have just had.
        await page.GotoAsync(app.BaseUrl + "/fighters", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });
        await AppFixture.WaitForAppAsync(page, errors);
        await page.Locator("tbody tr", new() { HasText = "AL" }).First.WaitForAsync(new() { Timeout = 30_000 });
        (await page.Locator("tbody tr", new() { HasText = "SM" }).CountAsync()).Should().BeGreaterThan(0, "both of them are on the card");
        (await page.Locator("tbody tr", new() { HasText = "AL" }).First.InnerTextAsync()).Should().Contain("1", "one fight each so far");

        // Their own page carries what the host would be told about them, in the same words.
        await page.Locator("tbody tr", new() { HasText = "AL" }).First.ClickAsync();
        await page.WaitForURLAsync(u => u.Contains("/fighters/AL", StringComparison.Ordinal), new() { Timeout = 30_000 });
        await page.Locator("section.style").WaitForAsync(new() { Timeout = 30_000 });
        (await page.Locator("section.style").InnerTextAsync()).Should().ContainEquivalentOf("1 debate", "one fight is an anecdote and the panel says so");
        await ShootAsync(page, "fighter-profile");

        errors.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task A_fight_can_be_stopped_by_the_room_and_the_microphone_closes_with_it()
    {
        var (context, page, errors) = await app.OpenAsync(AppFixture.Desktop, "/2p", user: "e2e-stopper");
        await using var _ = context;

        await TagBox(page, "player1Tag").WaitForAsync(new() { Timeout = 30_000 });
        await TypeTagAsync(page, "player1Tag", "ZA");
        await TypeTagAsync(page, "player2Tag", "ZB");
        await AppFixture.ToTheLastStepAsync(page);
        await page.GetByRole(AriaRole.Button, new() { Name = "Start the fight" }).ClickAsync();

        await page.Locator(".meter--on").WaitForAsync(new() { Timeout = 60_000 });
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
        (await page.Locator(".meter--on").CountAsync()).Should().Be(0, "there is nothing to record into");

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
