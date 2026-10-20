using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PoFightJudge.Api.Features.Auth;
using PoFightJudge.Api.Features.Fighters;
using PoFightJudge.Api.Features.Profiles;
using PoFightJudge.Api.Features.Watch;
using PoFightJudge.Shared;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;
using PoFightJudge.Shared.Validators;

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

    /// <summary>
    /// Casts two personas, so a match has real profiles to argue from. Straight into the store: nobody creates a
    /// profile through the API any more, so a test puts its cast where the seed would.
    /// </summary>
    private async Task<(MatchSide Husband, MatchSide Wife)> CastAsync(string suffix)
    {
        var profiles = factory.Services.GetRequiredService<IProfileRepository>();

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
            await profiles.UpsertAsync(persona.ToDomain());
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
        var (husband, wife) = await CastAsync("02");

        var response = await client.PostAsJsonAsync(ApiRoutes.Watch.VerdictUrl, new VerdictRequest(husband, wife, Transcript(6), "the thermostat"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var verdict = (await response.Content.ReadFromJsonAsync<VerdictResponse>())!;
        verdict.Persisted.Should().BeTrue();
        verdict.MatchId.Value.Should().NotBeNullOrWhiteSpace();
        verdict.Verdict.Should().NotBeNullOrWhiteSpace();
        verdict.Winner.Should().BeOneOf(husband.Id, wife.Id, string.Empty);
        verdict.HusbandStats.Should().NotBe(AdvancedStatsDto.Empty, "the analytics come from what was actually said");
        verdict.HusbandScore.Should().BeInRange(0, 100);

        // A profile's stats are its games: both of them now carry this one on their record.
        var cast = (await client.GetFromJsonAsync<List<ProfileDto>>(ApiRoutes.Profiles.Base))!;
        cast.Single(p => string.Equals(p.Persona.Initials, husband.Id, StringComparison.Ordinal)).Record.Matches.Should().BeGreaterThanOrEqualTo(1);
        cast.Single(p => string.Equals(p.Persona.Initials, wife.Id, StringComparison.Ordinal)).Record.Matches.Should().BeGreaterThanOrEqualTo(1);

        // The client holds the match and posts it back, so every part of it is bounded before anything is paid for.
        var endless = new WatchRoundDto("husband", new string('x', WatchLimits.MaxLineLength + 1), "angry");
        (await client.PostAsJsonAsync(ApiRoutes.Watch.VerdictUrl, new VerdictRequest(husband, wife, [endless], "the thermostat")))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "a line has a length");
        var noise = Round("husband", 0) with { Audio = new TtsAudioDto("not base64 at all!", "mp3") };
        (await client.PostAsJsonAsync(ApiRoutes.Watch.VerdictUrl, new VerdictRequest(husband, wife, [noise], "the thermostat")))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "audio that will not decode is refused before the judge is asked");
        (await client.PostAsJsonAsync(ApiRoutes.Watch.GenerateRoundStreamUrl, new GenerateRoundRequest(husband, wife, Transcript(WatchTurns.MaxLines), "husband")))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "the streamed route refuses a full match the same way the plain one does");
    }

    [Fact]
    public async Task A_person_arguing_a_persona_is_recorded_under_their_tag_so_their_profile_can_grow()
    {
        using var client = User("watch-human");
        var (husband, _) = await CastAsync("04");
        var person = MatchSide.Human("KDX", "Kim");

        var response = await client.PostAsJsonAsync(ApiRoutes.Watch.VerdictUrl, new VerdictRequest(husband, person, Transcript(6), "the thermostat"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<VerdictResponse>())!.Persisted.Should().BeTrue(
            "arguing an AI persona counts towards a person's record exactly as arguing another person does");

        // A person plays under exactly three letters; anything else is refused before the judge is asked.
        foreach (var tag in new[] { "KD", "K1D" })
        {
            (await client.PostAsJsonAsync(ApiRoutes.Watch.VerdictUrl, new VerdictRequest(husband, MatchSide.Human(tag, "Kim"), Transcript(6), "the thermostat")))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest, $"{tag} is not three letters");
        }

        // And what they said is kept as words, not only as a reading of them, so the persona written after any
        // later debate has this night to read as well.
        var said = await factory.Services.GetRequiredService<IFighterWordsRepository>().ListAsync(FighterId.From("KDX"));
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
