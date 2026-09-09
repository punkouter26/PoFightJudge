using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using PoFightJudge.Api.Features.Auth;
using PoFightJudge.Api.Features.Fighters;
using PoFightJudge.Api.Features.Records;
using PoFightJudge.Shared;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.E2EAPI;

/// <summary>
/// History, leaderboards and fighter pages: everything read back from the rows a debate left behind.
/// </summary>
[Collection(ApiCollection.Name)]
public class RecordsTests(ApiFactory factory)
{
    private HttpClient User(string id)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(FakeAuthOptions.UserHeader, id);
        return client;
    }

    /// <summary>Writes a finished debate straight into storage: these tests are about reading, not about arguing.</summary>
    private async Task<MatchId> RecordAsync(string userId, string one, string two, int score = 60, bool oneWon = true, MatchMode mode = MatchMode.Fight)
    {
        var matches = factory.Services.GetRequiredService<IMatchRepository>();
        var results = factory.Services.GetRequiredService<IFighterResultRepository>();
        var fighters = factory.Services.GetRequiredService<IFighterRepository>();
        var now = factory.Services.GetRequiredService<TimeProvider>().GetUtcNow();
        var id = MatchId.New();

        // Tags are normalised everywhere they are created, so a stored row carries the normalised form.
        one = Initials.Normalize(one);
        two = Initials.Normalize(two);

        await matches.UpsertAsync(new MatchDto(
            id, userId, mode, now.AddMinutes(-5), now, "the thermostat",
            MatchSide.Human(one), MatchSide.Human(two), SessionPhase.Done, SessionStatus.Ready,
            oneWon ? one : two, "It was close.", IsFake: true));

        await fighters.EnsureAsync(FighterId.From(one), now, role: null);
        await fighters.EnsureAsync(FighterId.From(two), now, role: null);
        await results.SaveAsync(
        [
            new FighterResultDto(one, userId, id, mode, now, "the thermostat", two, oneWon, false, score,
                StyleSnapshot.Empty with { Tone = "clipped", Opener = "Look, the thing is", Cefr = "B2" }),
            new FighterResultDto(two, userId, id, mode, now, "the thermostat", one, !oneWon, false, 100 - score, StyleSnapshot.Empty),
        ]);

        return id;
    }

    [Fact]
    public async Task History_is_only_what_the_signed_in_person_was_part_of()
    {
        var mine = User("records-mine");
        var theirs = User("records-theirs");
        var id = await RecordAsync("records-mine", "ha1", "ha2");
        await RecordAsync("records-theirs", "ha3", "ha4");

        var history = await mine.GetFromJsonAsync<MatchPageDto>(ApiRoutes.Matches.Base);

        history!.Matches.Select(m => m.Id).Should().Contain(id);
        history.Matches.Should().OnlyContain(m => string.Equals(m.UserId, "records-mine", StringComparison.Ordinal));
        (await theirs.GetFromJsonAsync<MatchPageDto>(ApiRoutes.Matches.Base))!.Matches.Should().NotContain(m => m.Id == id);
    }

    [Fact]
    public async Task Deleting_a_debate_takes_everything_it_produced_with_it()
    {
        var client = User("records-delete");
        var id = await RecordAsync("records-delete", "de1", "de2");
        var results = factory.Services.GetRequiredService<IFighterResultRepository>();
        (await results.ListForAsync(FighterId.From("DE1"), "records-delete")).Should().NotBeEmpty();

        var deleted = await client.DeleteAsync(ApiRoutes.Matches.ById(id));

        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync(ApiRoutes.Matches.ById(id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await results.ListForAsync(FighterId.From("DE1"), "records-delete")).Should().BeEmpty("a record that outlived its debate is a record of nothing");
        (await results.ListForAsync(FighterId.From("DE2"), "records-delete")).Should().BeEmpty();
    }

    [Fact]
    public async Task The_roster_carries_each_persons_record_so_a_list_of_them_is_worth_reading()
    {
        var client = User("records-roster");
        await RecordAsync("records-roster", "ro1", "ro2", score: 75);
        await RecordAsync("records-roster", "ro1", "ro2", score: 65);

        var roster = await client.GetFromJsonAsync<IReadOnlyList<FighterStatsDto>>(ApiRoutes.Fighters.RosterUrl);

        var one = roster!.Single(r => string.Equals(r.Tag, "RO1", StringComparison.Ordinal));
        one.Fights.Should().Be(2);
        one.Wins.Should().Be(2);
        one.Form.Should().Equal(75, 65);
        roster.Should().Contain(r => string.Equals(r.Tag, "RO2", StringComparison.Ordinal), "somebody who has only lost is still on the roster");
        roster.Should().BeInDescendingOrder(r => r.Fights);
    }

    [Fact]
    public async Task Records_need_signing_in()
    {
        var anonymous = factory.CreateClient();

        (await anonymous.GetAsync(ApiRoutes.Matches.Base)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync(ApiRoutes.Leaderboard.FightUrl)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync(ApiRoutes.Fighters.Base)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// A ruling is the one thing here worth sending to somebody. Sharing is asked for, the link reads without an
    /// account, and it can be taken back — nothing becomes readable merely by being played.
    /// </summary>
    [Fact]
    public async Task A_shared_ruling_reads_without_an_account_and_stops_when_it_is_taken_back()
    {
        var mine = User("records-share");
        var id = await RecordAsync("records-share", "sr1", "sr2");
        using var anonymous = factory.CreateClient();

        var shared = await mine.PostAsync(ApiRoutes.Matches.Share(id), content: null);
        var link = await shared.Content.ReadFromJsonAsync<ShareResponse>();

        shared.StatusCode.Should().Be(HttpStatusCode.OK);
        link!.Token.Should().NotBeNullOrWhiteSpace();
        link.Path.Should().Be($"/v/{link.Token}");

        var read = await anonymous.GetFromJsonAsync<SharedMatchDto>(ApiRoutes.Shares.ByToken(link.Token));
        read!.Topic.Should().Be("the thermostat");
        read.Side1Name.Should().Be("SR1");
        read.Winner.Should().Be("SR1");

        (await mine.DeleteAsync(ApiRoutes.Matches.Share(id))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await anonymous.GetAsync(ApiRoutes.Shares.ByToken(link.Token))).StatusCode
            .Should().Be(HttpStatusCode.NotFound, "a share that cannot be taken back is not a share");
    }
    /// <summary>
    /// History used to answer with every match a person had ever had, on every load, with no way to narrow it. The
    /// page is what keeps the answer small; the search is what makes a long history usable at all.
    /// </summary>
    [Fact]
    public async Task History_comes_back_a_page_at_a_time()
    {
        var client = User("records-paging");
        for (var i = 0; i < 5; i++)
        {
            await RecordAsync("records-paging", $"p{i}a", $"p{i}b");
        }

        var first = await client.GetFromJsonAsync<MatchPageDto>($"{ApiRoutes.Matches.Base}?skip=0&take=2");
        var second = await client.GetFromJsonAsync<MatchPageDto>($"{ApiRoutes.Matches.Base}?skip=2&take=2");

        first!.Matches.Should().HaveCount(2);
        first.Total.Should().Be(5, "the count is of everything behind the page, not of the page");
        second!.Matches.Should().HaveCount(2);
        second.Matches.Should().NotIntersectWith(first.Matches);
    }
}
