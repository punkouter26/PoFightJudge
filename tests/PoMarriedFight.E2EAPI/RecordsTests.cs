using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using PoMarriedFight.Api.Features.Auth;
using PoMarriedFight.Api.Features.Fighters;
using PoMarriedFight.Api.Features.Records;
using PoMarriedFight.Shared;
using PoMarriedFight.Shared.Identifiers;
using PoMarriedFight.Shared.Models;

namespace PoMarriedFight.E2EAPI;

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

        await fighters.EnsureAsync(FighterId.From(one), now);
        await fighters.EnsureAsync(FighterId.From(two), now);
        await results.SaveAsync(
        [
            new FighterResultDto(one, id, mode, now, "the thermostat", two, oneWon, false, score,
                StyleSnapshot.Empty with { Tone = "clipped", Opener = "Look, the thing is", Cefr = "B2" }),
            new FighterResultDto(two, id, mode, now, "the thermostat", one, !oneWon, false, 100 - score, StyleSnapshot.Empty),
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

        var history = await mine.GetFromJsonAsync<IReadOnlyList<MatchDto>>(ApiRoutes.Matches.Base);

        history!.Select(m => m.Id).Should().Contain(id);
        history.Should().OnlyContain(m => string.Equals(m.UserId, "records-mine", StringComparison.Ordinal));
        (await theirs.GetFromJsonAsync<IReadOnlyList<MatchDto>>(ApiRoutes.Matches.Base))!.Should().NotContain(m => m.Id == id);
    }

    [Fact]
    public async Task History_can_be_narrowed_to_one_kind_of_argument()
    {
        var client = User("records-modes");
        await RecordAsync("records-modes", "mo1", "mo2", mode: MatchMode.Fight);
        await RecordAsync("records-modes", "mo3", "mo4", mode: MatchMode.Watch);

        var fights = await client.GetFromJsonAsync<IReadOnlyList<MatchDto>>($"{ApiRoutes.Matches.Base}?mode={MatchMode.Fight}");

        fights!.Should().OnlyContain(m => m.Mode == MatchMode.Fight).And.NotBeEmpty();
    }

    [Fact]
    public async Task Deleting_a_debate_takes_everything_it_produced_with_it()
    {
        var client = User("records-delete");
        var id = await RecordAsync("records-delete", "de1", "de2");
        var results = factory.Services.GetRequiredService<IFighterResultRepository>();
        (await results.ListForAsync(FighterId.From("DE1"))).Should().NotBeEmpty();

        var deleted = await client.DeleteAsync(ApiRoutes.Matches.ById(id));

        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync(ApiRoutes.Matches.ById(id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await results.ListForAsync(FighterId.From("DE1"))).Should().BeEmpty("a record that outlived its debate is a record of nothing");
        (await results.ListForAsync(FighterId.From("DE2"))).Should().BeEmpty();
    }

    [Fact]
    public async Task Somebody_elses_debate_cannot_be_read_or_deleted()
    {
        var theirs = User("records-stranger");
        var id = await RecordAsync("records-owner", "st1", "st2");

        (await theirs.GetAsync(ApiRoutes.Matches.ById(id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await theirs.DeleteAsync(ApiRoutes.Matches.ById(id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_board_ranks_by_how_often_somebody_wins_and_ignores_a_single_night()
    {
        var client = User("records-board");
        await RecordAsync("records-board", "wi1", "wi2", score: 80);
        await RecordAsync("records-board", "wi1", "wi2", score: 85);
        await RecordAsync("records-board", "wi3", "wi4", score: 90);

        var board = await client.GetFromJsonAsync<IReadOnlyList<LeaderboardRowDto>>(ApiRoutes.Leaderboard.FightUrl);

        board!.Should().Contain(r => string.Equals(r.Id, "WI1", StringComparison.Ordinal));
        board.Should().NotContain(r => string.Equals(r.Id, "WI3", StringComparison.Ordinal), "one debate is not enough to rank anybody");
        board.Should().BeInDescendingOrder(r => r.WinRate);
        board.Single(r => string.Equals(r.Id, "WI1", StringComparison.Ordinal)).Wins.Should().Be(2);
    }

    [Fact]
    public async Task A_fighters_page_carries_their_record_and_how_they_argue()
    {
        var client = User("records-profile");
        await RecordAsync("records-profile", "pr1", "pr2", score: 70);
        await RecordAsync("records-profile", "pr1", "pr2", score: 60);

        var profile = await client.GetFromJsonAsync<FighterProfileDto>(ApiRoutes.Fighters.Profile(FighterId.From("PR1")));

        profile!.Fighter.Tag.Should().Be("PR1");
        profile.Stats.Fights.Should().Be(2);
        profile.Stats.Wins.Should().Be(2);
        profile.Stats.TopRival!.Opponent.Should().Be("PR2");
        profile.Style.Debates.Should().Be(2);
        profile.Style.Digest.Should().Contain("2 fights");
        profile.Results.Should().HaveCount(2);
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
    public async Task A_fighter_can_be_renamed_but_never_re_tagged()
    {
        var client = User("records-rename");
        await RecordAsync("records-rename", "re1", "re2");

        var renamed = await client.PutAsJsonAsync(ApiRoutes.Fighters.ByTag(FighterId.From("RE1")), new RenameFighterRequest("Alex"));

        renamed.StatusCode.Should().Be(HttpStatusCode.OK);
        var fighter = await renamed.Content.ReadFromJsonAsync<FighterDto>();
        fighter!.DisplayName.Should().Be("Alex");
        fighter.Tag.Should().Be("RE1", "the tag is the identity every result is keyed on");
    }

    [Fact]
    public async Task Deleting_a_fighter_clears_their_record_and_leaves_the_debates_alone()
    {
        var client = User("records-forget");
        var id = await RecordAsync("records-forget", "fo1", "fo2");
        var results = factory.Services.GetRequiredService<IFighterResultRepository>();

        var deleted = await client.DeleteAsync(ApiRoutes.Fighters.ByTag(FighterId.From("FO1")));

        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await results.ListForAsync(FighterId.From("FO1"))).Should().BeEmpty();
        (await results.ListForAsync(FighterId.From("FO2"))).Should().NotBeEmpty("the other one argued in it too");
        (await client.GetAsync(ApiRoutes.Matches.ById(id))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_fighter_nobody_has_heard_of_is_not_found()
    {
        var client = User("records-unknown");
        var tag = FighterId.From("ZZZ");

        (await client.GetAsync(ApiRoutes.Fighters.ByTag(tag))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync(ApiRoutes.Fighters.Profile(tag))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.DeleteAsync(ApiRoutes.Fighters.ByTag(tag))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Records_need_signing_in()
    {
        var anonymous = factory.CreateClient();

        (await anonymous.GetAsync(ApiRoutes.Matches.Base)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync(ApiRoutes.Leaderboard.FightUrl)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync(ApiRoutes.Fighters.Base)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
