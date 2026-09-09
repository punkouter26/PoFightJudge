using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PoFightJudge.Api.Features.Auth;
using PoFightJudge.Api.Features.Fighters;
using PoFightJudge.Api.Features.Watch;
using PoFightJudge.Shared;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.E2EAPI;

[Collection(ApiCollection.Name)]
public class WatchTests(ApiFactory factory)
{
    /// <summary>A client signed in as one caller. The rate limit partitions by user, so a dedicated id isolates a test.</summary>
    private HttpClient User(string id)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(FakeAuthOptions.UserHeader, id);
        return client;
    }

    /// <summary>Casts two personas, so a match has real profiles to argue from.</summary>
    private static async Task<(MatchSide Husband, MatchSide Wife)> CastAsync(HttpClient client, string suffix)
    {
        async Task<MatchSide> CreateAsync(string initials, ProfileRole role, string name)
        {
            var persona = new CreateProfileRequest
            {
                Initials = initials,
                Role = role,
                Name = name,
                Likes = "quiet mornings and a tidy hallway",
                Dislikes = "being interrupted mid-sentence",
                CommonArguments = "the thermostat",
                Philosophy = "say it once",
                TtsSettings = new TtsSettingsDto { VoiceName = role == ProfileRole.Wife ? "Kore" : "Charon" },
            };
            var response = await client.PostAsJsonAsync(ApiRoutes.Profiles.Base, persona);
            response.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.Conflict);
            return MatchSide.Persona(initials, name);
        }

        return (await CreateAsync($"H{suffix}", ProfileRole.Husband, "Matthew"), await CreateAsync($"W{suffix}", ProfileRole.Wife, "Kimberly"));
    }

    private static WatchRoundDto Round(string speaker, int index) =>
        new(speaker, $"Line {index} about the thermostat, and it is the third time this month.", "angry");

    private static List<WatchRoundDto> Transcript(int lines) =>
        [.. Enumerable.Range(0, lines).Select(i => Round(i % 2 == 0 ? "husband" : "wife", i))];

    [Fact]
    public async Task A_full_match_is_judged_and_persisted_with_a_result_for_each_persona()
    {
        using var client = User("watch-verdict");
        var (husband, wife) = await CastAsync(client, "02");

        var response = await client.PostAsJsonAsync(ApiRoutes.Watch.VerdictUrl, new VerdictRequest(husband, wife, Transcript(6), "the thermostat"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var verdict = (await response.Content.ReadFromJsonAsync<VerdictResponse>())!;
        verdict.Persisted.Should().BeTrue();
        verdict.MatchId.Value.Should().NotBeNullOrWhiteSpace();
        verdict.Verdict.Should().NotBeNullOrWhiteSpace();
        verdict.Winner.Should().BeOneOf(husband.Id, wife.Id, string.Empty);
        verdict.HusbandStats.Should().NotBe(AdvancedStatsDto.Empty, "the analytics come from what was actually said");
        verdict.HusbandScore.Should().BeInRange(0, 100);
    }

    [Fact]
    public async Task A_person_arguing_a_persona_is_recorded_under_their_tag_so_their_profile_can_grow()
    {
        using var client = User("watch-human");
        var (husband, _) = await CastAsync(client, "04");
        var person = MatchSide.Human("KD", "Kim");

        var response = await client.PostAsJsonAsync(ApiRoutes.Watch.VerdictUrl, new VerdictRequest(husband, person, Transcript(6), "the thermostat"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<VerdictResponse>())!.Persisted.Should().BeTrue(
            "arguing an AI persona counts towards a person's record exactly as arguing another person does");

        // And what they said is kept as words, not only as a reading of them, so the persona written after any
        // later debate has this night to read as well.
        var said = await factory.Services.GetRequiredService<IFighterWordsRepository>().ListAsync(FighterId.From("KD"));
        said.Should().ContainSingle().Which.Said.Should().Contain("the thermostat");
        said[0].Mode.Should().Be(MatchMode.Watch);

        // The person's turn is theirs to speak: the server refuses to write their line for them.
        var speakingForThem = await client.PostAsJsonAsync(ApiRoutes.Watch.GenerateRoundUrl, new GenerateRoundRequest(husband, person, [], "wife"));
        speakingForThem.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await speakingForThem.Content.ReadFromJsonAsync<HttpValidationProblemDetails>())!.Errors.Keys.Should().Contain("speaker");
    }

    private static async Task<ProfileDto> WaitForPersonaAsync(HttpClient client, string initials)
    {
        const int Attempts = 100; // ten seconds, the same patience the 2P persona is given
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var cast = await client.GetFromJsonAsync<List<ProfileDto>>(ApiRoutes.Profiles.Base) ?? [];
            if (cast.Find(p => string.Equals(p.Persona.Initials, initials, StringComparison.Ordinal)) is { } persona)
            {
                return persona;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"No persona was written for {initials} within 10s.");
    }

    [Fact]
    public async Task The_watch_routes_need_a_signed_in_caller()
    {
        using var anonymous = factory.CreateClient();

        (await anonymous.PostAsJsonAsync(ApiRoutes.Watch.GenerateRoundUrl, new GenerateRoundRequest(MatchSide.Persona("MAH"), MatchSide.Persona("KSH"), [], "husband")))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync(ApiRoutes.Watch.VerdictUrl, new VerdictRequest(MatchSide.Persona("MAH"), MatchSide.Persona("KSH"), [], null)))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
