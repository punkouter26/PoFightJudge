using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using PoFightJudge.Api.Features.Auth;
using PoFightJudge.Api.Features.Fight;
using PoFightJudge.Api.Features.Fighters;
using PoFightJudge.Api.Features.Records;
using PoFightJudge.Shared;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.E2EAPI;

/// <summary>
/// Starting, watching and stopping a fight over HTTP. The live half runs on the scripted host, so the whole thing
/// works with no key.
/// </summary>
[Collection(ApiCollection.Name)]
public class FightTests(ApiFactory factory)
{
    private HttpClient User(string id)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(FakeAuthOptions.UserHeader, id);
        return client;
    }

    private static async Task<MatchId> StartAsync(HttpClient client, string one, string two, string? topic = "the thermostat")
    {
        var response = await client.PostAsJsonAsync(ApiRoutes.Fights.Base, new CreateFightRequest(HostPersonaId.Referee, one, two, topic));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var started = await response.Content.ReadFromJsonAsync<CreateFightResponse>();
        started.Should().NotBeNull();
        return started!.MatchId;
    }

    [Fact]
    public async Task The_host_is_told_how_each_of_them_argues_and_a_stranger_is_called_a_first_fight()
    {
        var client = User("fight-digest");
        var results = factory.Services.GetRequiredService<IFighterResultRepository>();
        var now = factory.Services.GetRequiredService<TimeProvider>().GetUtcNow();
        var known = FighterId.From("DG1");

        // Two debates behind them, so the digest is a read rather than a guess.
        foreach (var day in new[] { -2, -1 })
        {
            await results.SaveAsync(
            [
                new FighterResultDto(known.Value, "fight-digest", MatchId.New(), MatchMode.Fight, now.AddDays(day), "the thermostat", "DG2",
                    true, false, 70, StyleSnapshot.Empty with { Tone = "clipped", Opener = "Look, the thing is", Cefr = "B2" }),
            ]);
        }

        var id = await StartAsync(client, known.Value, "dg9");

        var setup = factory.Services.GetRequiredService<SessionRegistry>().Get(id)!.Setup;
        setup.Player1Digest.Should().Contain("2 fights").And.Contain("clipped");
        setup.Player2Digest.Should().Be("First fight.", "a host told nothing about a stranger would invent them");
    }

    [Fact]
    public async Task Starting_a_fight_creates_both_fighters_and_answers_with_somewhere_to_join()
    {
        var client = User("fight-start");

        var response = await client.PostAsJsonAsync(
            ApiRoutes.Fights.Base,
            new CreateFightRequest(HostPersonaId.Referee, "aa1", "bb2", null, ProfileRole.Wife, ProfileRole.Husband));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await response.Content.ReadFromJsonAsync<CreateFightResponse>())!.MatchId;

        // Read through the store rather than over HTTP: the roster endpoint is a later task, but the rows are the point.
        var fighters = factory.Services.GetRequiredService<IFighterRepository>();
        var one = await fighters.GetAsync(FighterId.From("AA1"));
        one.Should().NotBeNull("both fighters exist from the moment the fight starts");
        one!.Role.Should().Be(ProfileRole.Wife, "the seat chosen at setup is what their persona will argue from");
        (await fighters.GetAsync(FighterId.From("BB2")))!.Role.Should().Be(ProfileRole.Husband);

        var snapshot = await client.GetFromJsonAsync<DebateSnapshotDto>(ApiRoutes.Fights.ById(id));
        snapshot.Should().NotBeNull();
        snapshot!.MatchId.Should().Be(id);
        snapshot.Phase.Should().Be(SessionPhase.Intro);
        snapshot.Player1Name.Should().Be("AA1");
        snapshot.Persona.Should().Be(HostPersonaId.Referee);

        await client.PostAsync(ApiRoutes.Fights.End(id), null);
    }

    [Fact]
    public async Task A_tag_that_is_missing_or_shared_is_refused_because_the_record_needs_a_name()
    {
        var client = User("fight-tags");

        foreach (var (one, two) in new[] { ("", "CD"), ("AB", ""), ("AB", "AB"), ("ab", "AB"), ("!!", "CD") })
        {
            var response = await client.PostAsJsonAsync(ApiRoutes.Fights.Base, new CreateFightRequest(HostPersonaId.Referee, one, two));
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"'{one}' against '{two}' is not two fighters");
        }
    }

    [Fact]
    public async Task A_host_nobody_has_heard_of_is_refused()
    {
        var client = User("fight-host");

        var response = await client.PostAsJsonAsync(
            ApiRoutes.Fights.Base,
            new { Persona = "Elvis", Player1Tag = "AB", Player2Tag = "CD" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Somebody_elses_fight_is_not_found_rather_than_forbidden()
    {
        var mine = User("fight-owner");
        var theirs = User("fight-stranger");
        var id = await StartAsync(mine, "cc1", "dd2");

        (await theirs.GetAsync(ApiRoutes.Fights.ById(id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await theirs.PostAsync(ApiRoutes.Fights.End(id), null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await mine.PostAsync(ApiRoutes.Fights.End(id), null);
    }

    [Fact]
    public async Task A_fight_that_never_happened_is_not_found()
    {
        var client = User("fight-missing");

        (await client.GetAsync(ApiRoutes.Fights.ById(MatchId.New()))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Ending_a_fight_stores_it_and_the_same_route_then_answers_with_the_record()
    {
        var client = User("fight-end");
        var id = await StartAsync(client, "ee1", "ff2", "who does the dishes");

        var ended = await client.PostAsync(ApiRoutes.Fights.End(id), null);
        ended.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var stored = await client.GetFromJsonAsync<MatchDto>(ApiRoutes.Fights.ById(id));
        stored.Should().NotBeNull();
        stored!.Mode.Should().Be(MatchMode.Fight);
        stored.Side1.Id.Should().Be("EE1");
        stored.Side2.Id.Should().Be("FF2");
        stored.Side1.IsHuman.Should().BeTrue("a fight is two people on one microphone");
        stored.IsFake.Should().BeTrue("this one was argued in front of the scripted host");
        stored.EndedAt.Should().NotBeNull();
        stored.Persona.Should().Be(nameof(HostPersonaId.Referee));

        // Ending it twice is what two tabs pressing stop looks like, and the second is simply no longer there.
        (await client.PostAsync(ApiRoutes.Fights.End(id), null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Starting_a_second_fight_ends_the_first_one()
    {
        var client = User("fight-replace");
        var first = await StartAsync(client, "gg1", "hh2");

        var second = await StartAsync(client, "ii1", "jj2");

        var replaced = await client.GetFromJsonAsync<MatchDto>(ApiRoutes.Fights.ById(first));
        replaced.Should().NotBeNull("the first fight was stored on its way out rather than dropped");
        replaced!.EndedAt.Should().NotBeNull();

        await client.PostAsync(ApiRoutes.Fights.End(second), null);
    }

    [Fact]
    public async Task A_fight_cannot_be_started_or_read_without_signing_in()
    {
        var anonymous = factory.CreateClient();

        (await anonymous.PostAsJsonAsync(ApiRoutes.Fights.Base, new CreateFightRequest(HostPersonaId.Referee, "AB", "CD")))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync(ApiRoutes.Fights.ById(MatchId.New()))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
