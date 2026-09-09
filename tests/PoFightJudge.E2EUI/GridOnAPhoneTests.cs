using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using PoFightJudge.Api.Features.Records;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.E2EUI;

/// <summary>
/// The list pages at 390 px. A RadzenDataGrid whose columns do not fit puts a horizontal scrollbar inside the page,
/// which on a phone is a table read by dragging sideways: /history's fixed widths alone came to 36rem before its
/// two flexible columns, and /fighters' to 46rem.
///
/// The rows are written straight into the store. An empty grid fits any screen, so a version of this test that did
/// not seed would have passed while saying nothing.
/// </summary>
[Collection(AppCollection.Name)]
public class GridOnAPhoneTests(AppFixture app)
{
    private const string User = "e2e-narrow";

    [SkippableFact]
    public async Task No_grid_scrolls_sideways_on_a_phone()
    {
        await RecordAsync(MatchMode.Watch, "the thermostat", MatchSide.Persona("MAH", "Married Husband"), MatchSide.Persona("KSH", "Karen"));
        await RecordAsync(MatchMode.Fight, "who forgot the bins and left them there all week", MatchSide.Human("HA1", "Alex"), MatchSide.Human("HA2", "Sam"));

        var offenders = new List<string>();
        foreach (var route in new[] { "/history", "/fighters", "/health" })
        {
            var (context, page, _) = await app.OpenAsync(AppFixture.Mobile, route, user: User);
            await using var _ = context;
            await page.WaitForTimeoutAsync(1200);

            var wide = await page.EvaluateAsync<string[]>(
                @"() => [...document.querySelectorAll('.rz-data-grid-data, .rz-grid-table, table')]
                    .filter(e => e.scrollWidth > e.clientWidth + 2 && e.clientWidth > 0)
                    .map(e => (e.className || e.tagName).split(' ')[0] + ' ' + e.scrollWidth + '>' + e.clientWidth)");
            offenders.AddRange(wide.Select(w => $"{route}: {w}"));
        }

        offenders.Should().BeEmpty("a phone reads a list by scrolling down it, not across it");
    }

    [SkippableFact]
    public async Task A_phone_gets_the_short_set_of_columns_and_a_desktop_the_whole_table()
    {
        await RecordAsync(MatchMode.Fight, "who forgot the bins", MatchSide.Human("HB1", "Alex"), MatchSide.Human("HB2", "Sam"));

        var (phone, phonePage, _) = await app.OpenAsync(AppFixture.Mobile, "/history", user: User);
        await using var _ = phone;
        await phonePage.Locator(".history tbody tr").First.WaitForAsync(new() { Timeout = 30_000 });
        var onAPhone = await phonePage.Locator(".history thead th").CountAsync();

        var (desk, deskPage, _) = await app.OpenAsync(AppFixture.Desktop, "/history", user: User);
        await using var __ = desk;
        await deskPage.Locator(".history tbody tr").First.WaitForAsync(new() { Timeout = 30_000 });
        var onADesktop = await deskPage.Locator(".history thead th").CountAsync();

        onAPhone.Should().BeLessThan(onADesktop, "the columns a phone cannot fit are dropped rather than squeezed");
        onAPhone.Should().BeGreaterThan(1, "what is left still has to identify the argument and let you open it");
    }

    private async Task RecordAsync(MatchMode mode, string topic, MatchSide one, MatchSide two)
    {
        var matches = app.Services.GetRequiredService<IMatchRepository>();
        var now = app.Services.GetRequiredService<TimeProvider>().GetUtcNow();
        await matches.UpsertAsync(new MatchDto(
            MatchId.New(), User, mode, now.AddMinutes(-6), now, topic, one, two,
            SessionPhase.Done, SessionStatus.Ready, one.DisplayName, "It was close.", IsFake: true));
    }
}
