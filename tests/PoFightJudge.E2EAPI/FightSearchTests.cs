using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using PoFightJudge.Api.Features.Ai;
using PoFightJudge.Api.Features.Auth;
using PoFightJudge.Api.Features.Records;
using PoFightJudge.Shared;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.E2EAPI;

/// <summary>
/// Finding a fight by what it was about, over the API.
/// </summary>
/// <remarks>
/// Runs on the deterministic embedding stand-in, which is a bag of words over fixed buckets — crude, and exactly
/// enough to be a similarity: two texts that share words point the same way and two that share none do not. That
/// is the property this endpoint rests on, and it can be asserted with no key and no network.
/// </remarks>
[Collection(ApiCollection.Name)]
public class FightSearchTests(ApiFactory factory)
{
    private HttpClient User(string id)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(FakeAuthOptions.UserHeader, id);
        return client;
    }

    /// <summary>A finished fight with its vector already attached, which is what the pipeline leaves behind.</summary>
    private async Task<MatchId> RememberAsync(string userId, string topic, string summary)
    {
        var matches = factory.Services.GetRequiredService<IMatchRepository>();
        var embedding = factory.Services.GetRequiredService<IGeminiEmbedding>();
        var now = factory.Services.GetRequiredService<TimeProvider>().GetUtcNow();
        var id = MatchId.New();

        await matches.UpsertAsync(new MatchDto(
            id, userId, MatchMode.Fight, now.AddMinutes(-5), now, topic,
            MatchSide.Human("AL"), MatchSide.Human("SM"), SessionPhase.Done, SessionStatus.Ready,
            "AL", summary, IsFake: true));

        var described = FightSearch.Describe(topic, "AL", "SM", summary);
        await matches.SaveVectorAsync(id, await embedding.EmbedAsync(described, forQuery: false), described);
        return id;
    }

    [Fact]
    public async Task A_fight_is_found_by_what_it_was_about_rather_than_by_its_topic_wording()
    {
        var user = $"search-{Guid.NewGuid():N}";
        var heating = await RememberAsync(user, "the heating bill", "AL argued the radiators were on all night while nobody was home.");
        await RememberAsync(user, "the dishes", "SM argued the dishwasher was never unloaded.");

        using var client = User(user);
        var found = await client.GetFromJsonAsync<FightSearchResponse>(
            $"{ApiRoutes.Matches.SearchUrl}?q=the%20radiators%20were%20on%20all%20night");

        found.Should().NotBeNull();
        found!.Available.Should().BeTrue();
        found.Found.Should().NotBeEmpty();
        found.Found[0].Id.Should().Be(heating, "nothing in that query is the word heating");
    }

    [Fact]
    public async Task One_account_never_finds_another_accounts_arguments()
    {
        var mine = $"search-{Guid.NewGuid():N}";
        var theirs = $"search-{Guid.NewGuid():N}";
        await RememberAsync(theirs, "the heating bill", "AL argued the radiators were on all night while nobody was home.");

        using var client = User(mine);
        var found = await client.GetFromJsonAsync<FightSearchResponse>(
            $"{ApiRoutes.Matches.SearchUrl}?q=the%20radiators%20were%20on%20all%20night");

        found!.Found.Should().BeEmpty("a history is one account's, and a search is a way of reading it");
    }

    [Fact]
    public async Task An_empty_search_asks_for_nothing_rather_than_returning_everything()
    {
        var user = $"search-{Guid.NewGuid():N}";
        await RememberAsync(user, "the heating bill", "AL argued the radiators were on all night.");

        using var client = User(user);
        var found = await client.GetFromJsonAsync<FightSearchResponse>($"{ApiRoutes.Matches.SearchUrl}?q=");

        found!.Found.Should().BeEmpty();
        found.Available.Should().BeFalse("nothing was searched for, so there is nothing to say about the results");
    }

    [Fact]
    public async Task Searching_a_history_with_nothing_in_it_finds_nothing_and_says_so_plainly()
    {
        using var client = User($"search-{Guid.NewGuid():N}");

        var found = await client.GetFromJsonAsync<FightSearchResponse>($"{ApiRoutes.Matches.SearchUrl}?q=the%20thermostat");

        found!.Available.Should().BeTrue("the search ran; there was simply nothing to find");
        found.Found.Should().BeEmpty();
    }

    [Fact]
    public async Task Signing_in_is_required_to_search_your_own_history()
    {
        using var anonymous = factory.CreateClient();

        using var response = await anonymous.GetAsync(new Uri($"{ApiRoutes.Matches.SearchUrl}?q=anything", UriKind.Relative));

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }
}
