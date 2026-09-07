using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using PoMarriedFight.Api.Features.Records;
using PoMarriedFight.Shared.Identifiers;
using PoMarriedFight.Shared.Models;

namespace PoMarriedFight.E2EUI;

/// <summary>
/// The history page over a real host: both kinds of argument in one list, and the delete that takes one away.
/// The arguments are written straight into the store — arguing two of them through the browser would be a test of
/// the other pages, and this one is about what happens afterwards.
/// </summary>
[Collection(AppCollection.Name)]
public class HistoryTests(AppFixture app)
{
    private const string User = "e2e-history";

    private async Task<MatchId> RecordAsync(MatchMode mode, string topic, MatchSide one, MatchSide two)
    {
        var matches = app.Services.GetRequiredService<IMatchRepository>();
        var now = app.Services.GetRequiredService<TimeProvider>().GetUtcNow();
        var id = MatchId.New();

        await matches.UpsertAsync(new MatchDto(
            id, User, mode, now.AddMinutes(-6), now, topic, one, two,
            SessionPhase.Done, SessionStatus.Ready, one.DisplayName, "It was close.", IsFake: true));

        return id;
    }

    [SkippableFact]
    public async Task Both_kinds_of_argument_are_listed_can_be_narrowed_and_one_can_be_deleted()
    {
        var watched = await RecordAsync(MatchMode.Watch, "the thermostat", MatchSide.Persona("MAH", "Married Husband"), MatchSide.Persona("KSH", "Karen"));
        await RecordAsync(MatchMode.Fight, "who forgot the bins", MatchSide.Human("HA1", "Alex"), MatchSide.Human("HA2", "Sam"));

        var (context, page, errors) = await app.OpenAsync(AppFixture.Desktop, "/history", user: User);
        await using var _ = context;

        var rows = page.Locator("tbody tr");
        await page.WaitForFunctionAsync("() => document.querySelectorAll('tbody tr').length === 2", null, new() { Timeout = 30_000 });
        (await page.Locator(".mode--watch").CountAsync()).Should().Be(1, "one of them was the cast arguing");
        (await page.Locator(".mode--fight").CountAsync()).Should().Be(1, "the other was two people on a microphone");

        // Narrowing goes back to the server, so what comes back is the filter working rather than rows being hidden.
        await page.GetByRole(AriaRole.Radio, new() { Name = "Fights" }).ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelectorAll('tbody tr').length === 1", null, new() { Timeout = 15_000 });
        (await rows.First.InnerTextAsync()).Should().Contain("who forgot the bins");

        await page.GetByRole(AriaRole.Radio, new() { Name = "All" }).ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelectorAll('tbody tr').length === 2", null, new() { Timeout = 15_000 });

        await page.GetByRole(AriaRole.Button, new() { Name = "Delete the thermostat" }).ClickAsync();
        var confirm = page.Locator(".rz-dialog");
        await confirm.WaitForAsync(new() { Timeout = 10_000 });
        (await confirm.InnerTextAsync()).Should().Contain("cannot be undone");
        await confirm.GetByRole(AriaRole.Button, new() { Name = "Delete" }).ClickAsync();

        await page.WaitForFunctionAsync("() => document.querySelectorAll('tbody tr').length === 1", null, new() { Timeout = 15_000 });
        (await page.Locator("tbody").InnerTextAsync()).Should().NotContain("the thermostat");

        var matches = app.Services.GetRequiredService<IMatchRepository>();
        (await matches.GetAsync(User, watched)).Should().BeNull("deleting the row deletes the argument, not just the line on screen");

        errors.Should().BeEmpty();
    }
}
