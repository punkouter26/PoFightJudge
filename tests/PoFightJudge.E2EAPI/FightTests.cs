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
    public async Task A_fight_cannot_be_started_or_read_without_signing_in()
    {
        var anonymous = factory.CreateClient();

        (await anonymous.PostAsJsonAsync(ApiRoutes.Fights.Base, new CreateFightRequest(HostPersonaId.Referee, "AB", "CD")))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync(ApiRoutes.Fights.ById(MatchId.New()))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
