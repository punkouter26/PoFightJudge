using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using PoMarriedFight.Api.Features.Auth;
using PoMarriedFight.Api.Features.Fighters;
using PoMarriedFight.Shared;
using PoMarriedFight.Shared.Identifiers;
using PoMarriedFight.Shared.Models;

namespace PoMarriedFight.E2EAPI;

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

        var id = await StartAsync(client, "aa1", "bb2");

        // Read through the store rather than over HTTP: the roster endpoint is a later task, but the rows are the point.
        var fighters = factory.Services.GetRequiredService<IFighterRepository>();
        (await fighters.GetAsync(FighterId.From("AA1"))).Should().NotBeNull("both fighters exist from the moment the fight starts");
        (await fighters.GetAsync(FighterId.From("BB2"))).Should().NotBeNull();

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
