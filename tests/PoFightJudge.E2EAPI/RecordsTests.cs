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
    public async Task History_can_be_narrowed_to_one_kind_of_argument()
    {
        var client = User("records-modes");
        await RecordAsync("records-modes", "mo1", "mo2", mode: MatchMode.Fight);
        await RecordAsync("records-modes", "mo3", "mo4", mode: MatchMode.Watch);

        var fights = await client.GetFromJsonAsync<MatchPageDto>($"{ApiRoutes.Matches.Base}?mode={MatchMode.Fight}");

        fights!.Matches.Should().OnlyContain(m => m.Mode == MatchMode.Fight).And.NotBeEmpty();
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

    /// <summary>
    /// The roster is shared on purpose — two people on one microphone are signed in as one of them — but what they
    /// argued about is not. Walking the three-letter tag space must not read a stranger's debates back to them.
    /// </summary>
    [Fact]
    public async Task A_fighters_record_is_only_what_this_account_saw_them_do()
    {
        var mine = User("records-mine-tag");
        await RecordAsync("records-mine-tag", "sh1", "sh2", score: 90);
        await RecordAsync("records-someone-else", "sh1", "sh3", score: 20, oneWon: false);

        var profile = await mine.GetFromJsonAsync<FighterProfileDto>(ApiRoutes.Fighters.Profile(FighterId.From("SH1")));

        profile!.Results.Should().ContainSingle("the other account's debate is theirs, not mine");
        profile.Results[0].Opponent.Should().Be("SH2");
        profile.Stats.Fights.Should().Be(1);
        profile.Stats.Wins.Should().Be(1, "the loss belongs to somebody else's night");

        var roster = await mine.GetFromJsonAsync<IReadOnlyList<FighterStatsDto>>(ApiRoutes.Fighters.RosterUrl);
        roster!.Should().Contain(r => string.Equals(r.Tag, "SH3", StringComparison.Ordinal), "the tag is on the shared roster");
        roster.Single(r => string.Equals(r.Tag, "SH3", StringComparison.Ordinal)).Fights
            .Should().Be(0, "but with no record, because none of it was mine");
    }

    [Fact]
    public async Task Forgetting_a_fighter_forgets_them_here_and_leaves_somebody_elses_record_alone()
    {
        var mine = User("records-forget-mine");
        await RecordAsync("records-forget-mine", "fm1", "fm2");
        await RecordAsync("records-forget-theirs", "fm1", "fm3");
        var results = factory.Services.GetRequiredService<IFighterResultRepository>();

        var deleted = await mine.DeleteAsync(ApiRoutes.Fighters.ByTag(FighterId.From("FM1")));

        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await results.ListForAsync(FighterId.From("FM1"), "records-forget-mine")).Should().BeEmpty();
        (await results.ListForAsync(FighterId.From("FM1"), "records-forget-theirs")).Should().NotBeEmpty("their record is not mine to delete");
        (await factory.Services.GetRequiredService<IFighterRepository>().GetAsync(FighterId.From("FM1")))
            .Should().NotBeNull("the tag stays on the roster while somebody still has a record under it");
    }

    [Fact]
    public async Task The_board_ranks_the_people_this_account_has_argued_with()
    {
        var mine = User("records-board-mine");
        await RecordAsync("records-board-mine", "bm1", "bm2");
        await RecordAsync("records-board-mine", "bm1", "bm2");
        await RecordAsync("records-board-theirs", "bt1", "bt2");
        await RecordAsync("records-board-theirs", "bt1", "bt2");

        var board = await mine.GetFromJsonAsync<IReadOnlyList<LeaderboardRowDto>>(ApiRoutes.Leaderboard.FightUrl);

        board!.Should().Contain(r => string.Equals(r.Id, "BM1", StringComparison.Ordinal));
        board.Should().NotContain(r => string.Equals(r.Id, "BT1", StringComparison.Ordinal), "somebody else's fights are not on my board");
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
        (await results.ListForAsync(FighterId.From("FO1"), "records-forget")).Should().BeEmpty();
        (await results.ListForAsync(FighterId.From("FO2"), "records-forget")).Should().NotBeEmpty("the other one argued in it too");
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

    /// <summary>
    /// A watch is stored as its lines, and the replay page is the only thing that reads them back. Without this the
    /// audio written for every round is write-only.
    /// </summary>
    [Fact]
    public async Task A_watch_can_be_read_back_line_by_line()
    {
        var client = User("records-turns");
        var id = await RecordAsync("records-turns", "tu1", "tu2", mode: MatchMode.Watch);
        var matches = factory.Services.GetRequiredService<IMatchRepository>();
        await matches.SaveTurnsAsync(id,
        [
            new TurnDto(id, 0, Speaker.Player1, TurnKind.Round, "You never load it properly.") { Mood = "clipped", AudioBlobName = "a", AudioFormat = "mp3" },
            new TurnDto(id, 1, Speaker.Player2, TurnKind.Round, "I load it exactly as the manual says.") { Mood = "flat" },
        ]);

        var turns = await client.GetFromJsonAsync<IReadOnlyList<TurnDto>>(ApiRoutes.Matches.Turns(id));

        turns!.Should().HaveCount(2).And.BeInAscendingOrder(t => t.Index);
        turns[0].Text.Should().Be("You never load it properly.");
        turns[0].Mood.Should().Be("clipped");
    }

    [Fact]
    public async Task Somebody_elses_lines_cannot_be_read_back()
    {
        var theirs = User("records-turns-stranger");
        var id = await RecordAsync("records-turns-owner", "tv1", "tv2", mode: MatchMode.Watch);

        (await theirs.GetAsync(ApiRoutes.Matches.Turns(id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
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

    [Fact]
    public async Task Sharing_twice_is_one_link_rather_than_two()
    {
        var mine = User("records-share-twice");
        var id = await RecordAsync("records-share-twice", "st3", "st4");

        var first = await (await mine.PostAsync(ApiRoutes.Matches.Share(id), content: null)).Content.ReadFromJsonAsync<ShareResponse>();
        var second = await (await mine.PostAsync(ApiRoutes.Matches.Share(id), content: null)).Content.ReadFromJsonAsync<ShareResponse>();

        second!.Token.Should().Be(first!.Token, "pressing share again wants the address, not a second live link");
    }

    [Fact]
    public async Task Nobody_can_share_a_debate_that_is_not_theirs()
    {
        var theirs = User("records-share-stranger");
        var id = await RecordAsync("records-share-owner", "sx1", "sx2");

        (await theirs.PostAsync(ApiRoutes.Matches.Share(id), content: null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await theirs.DeleteAsync(ApiRoutes.Matches.Share(id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>A link that outlived the argument it points at would be the one way a delete failed to be a delete.</summary>
    [Fact]
    public async Task Deleting_the_argument_kills_the_link_to_it()
    {
        var mine = User("records-share-delete");
        var id = await RecordAsync("records-share-delete", "sd1", "sd2");
        using var anonymous = factory.CreateClient();
        var link = await (await mine.PostAsync(ApiRoutes.Matches.Share(id), content: null)).Content.ReadFromJsonAsync<ShareResponse>();

        await mine.DeleteAsync(ApiRoutes.Matches.ById(id));

        (await anonymous.GetAsync(ApiRoutes.Shares.ByToken(link!.Token))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// What travels is the ruling, not a dossier. A recording of two real people arguing, and the twenty-five things
    /// measured about how they did it, are not something a link passed around should carry.
    /// </summary>
    [Fact]
    public async Task A_shared_ruling_carries_no_recording_and_no_measurements()
    {
        var mine = User("records-share-thin");
        var id = await RecordAsync("records-share-thin", "sn1", "sn2");
        using var anonymous = factory.CreateClient();
        var link = await (await mine.PostAsync(ApiRoutes.Matches.Share(id), content: null)).Content.ReadFromJsonAsync<ShareResponse>();

        var body = await anonymous.GetStringAsync(ApiRoutes.Shares.ByToken(link!.Token));

        body.Should().NotContain("records-share-thin", "the account that owns it is nobody else's business");
        body.Should().NotContain("AudioBlobName").And.NotContain("audio");
        body.Should().NotContain("Metrics").And.NotContain("metrics");
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

    [Fact]
    public async Task A_hand_written_take_cannot_pull_the_whole_table()
    {
        var client = User("records-take");
        await RecordAsync("records-take", "tk1", "tk2");

        var page = await client.GetFromJsonAsync<MatchPageDto>($"{ApiRoutes.Matches.Base}?take=100000");

        page!.Take.Should().BeLessThanOrEqualTo(MatchQuery.MaxTake);
    }

    [Fact]
    public async Task History_can_be_searched_by_topic_by_name_and_by_tag()
    {
        var client = User("records-search");
        var id = await RecordAsync("records-search", "se1", "se2");

        var byTopic = await client.GetFromJsonAsync<MatchPageDto>($"{ApiRoutes.Matches.Base}?q=thermo");
        var byTag = await client.GetFromJsonAsync<MatchPageDto>($"{ApiRoutes.Matches.Base}?q=se2");
        var byNothing = await client.GetFromJsonAsync<MatchPageDto>($"{ApiRoutes.Matches.Base}?q=zzzznothing");

        byTopic!.Matches.Should().Contain(m => m.Id == id);
        byTag!.Matches.Should().Contain(m => m.Id == id, "a tag is how somebody looks for their own arguments");
        byNothing!.Matches.Should().BeEmpty();
        byNothing.Total.Should().Be(0);
    }

    [Fact]
    public async Task History_can_be_narrowed_to_a_stretch_of_time()
    {
        var client = User("records-dates");
        var id = await RecordAsync("records-dates", "dt1", "dt2");
        var now = factory.Services.GetRequiredService<TimeProvider>().GetUtcNow();

        var inside = await client.GetFromJsonAsync<MatchPageDto>(
            $"{ApiRoutes.Matches.Base}?from={Uri.EscapeDataString(now.AddDays(-1).ToString("O"))}");
        var after = await client.GetFromJsonAsync<MatchPageDto>(
            $"{ApiRoutes.Matches.Base}?from={Uri.EscapeDataString(now.AddDays(1).ToString("O"))}");

        inside!.Matches.Should().Contain(m => m.Id == id);
        after!.Matches.Should().NotContain(m => m.Id == id);
    }
}
