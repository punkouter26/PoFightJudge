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
    public async Task A_round_is_generated_for_the_persona_whose_turn_it_is_with_an_attitude_from_their_sliders()
    {
        using var client = User("watch-round");
        var (husband, wife) = await CastAsync(client, "01");

        var response = await client.PostAsJsonAsync(ApiRoutes.Watch.GenerateRoundUrl, new GenerateRoundRequest(husband, wife, [], "husband", "the thermostat"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var round = (await response.Content.ReadFromJsonAsync<GenerateRoundResponse>())!;
        round.Text.Should().NotBeNullOrWhiteSpace();
        WatchRules.Moods.Should().Contain(round.Mood);
        round.Attitude.Should().NotBeNullOrWhiteSpace();
        round.IsFake.Should().BeTrue("the test host runs the fakes");
    }

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
    public async Task A_slapped_match_carries_seven_lines_and_an_eighth_is_refused()
    {
        using var client = User("watch-slap");
        var (husband, wife) = await CastAsync(client, "03");

        var slapped = await client.PostAsJsonAsync(ApiRoutes.Watch.VerdictUrl, new VerdictRequest(husband, wife, Transcript(7), "the thermostat"));
        slapped.StatusCode.Should().Be(HttpStatusCode.OK, "a slap inserts one extra reaction, so seven lines is a legitimate match");

        var tooLong = await client.PostAsJsonAsync(ApiRoutes.Watch.VerdictUrl, new VerdictRequest(husband, wife, Transcript(8), "the thermostat"));
        tooLong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = (await tooLong.Content.ReadFromJsonAsync<HttpValidationProblemDetails>())!;
        problem.Errors.Keys.Should().Contain("rounds");

        var empty = await client.PostAsJsonAsync(ApiRoutes.Watch.VerdictUrl, new VerdictRequest(husband, wife, [], "the thermostat"));
        empty.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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

    [Fact]
    public async Task A_person_who_argued_a_persona_comes_out_of_it_as_one_the_other_channels_can_seat()
    {
        using var client = User("watch-persona");
        var (husband, _) = await CastAsync(client, "07");
        var person = MatchSide.Human("KX", "Kim");

        var response = await client.PostAsJsonAsync(ApiRoutes.Watch.VerdictUrl, new VerdictRequest(husband, person, Transcript(6), "the thermostat"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Queued behind the reply on purpose — the room is not kept waiting on a model call for a card — so it is
        // given the same moment the 2P persona is given.
        var persona = await WaitForPersonaAsync(client, "KX");
        persona.FromFights.Should().BeTrue("a 1P debate builds their profile exactly as a 2P fight does");
        persona.Persona.Role.Should().Be(ProfileRole.Wife, "the seat they argued from");
        persona.Persona.Name.Should().Be("KX", "the roster names them, not the match");
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
    public async Task A_speaker_who_is_neither_side_is_refused_rather_than_quietly_becoming_the_wife()
    {
        using var client = User("watch-speaker");
        var (husband, wife) = await CastAsync(client, "09");

        // The husband's own initials name a persona, not a side. Everything downstream reads "not the husband" as
        // the wife, so an unrecognised speaker would quietly generate the wrong side's line — and the statistics
        // built from "whose line was this" would then count nothing at all.
        var response = await client.PostAsJsonAsync(ApiRoutes.Watch.GenerateRoundUrl, new GenerateRoundRequest(husband, wife, [], husband.Id));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>())!.Errors.Keys.Should().Contain("speaker");
    }

    [Fact]
    public async Task Two_people_are_a_fight_and_the_same_side_twice_is_neither()
    {
        using var client = User("watch-sides");
        var (husband, _) = await CastAsync(client, "05");

        var twoPeople = await client.PostAsJsonAsync(ApiRoutes.Watch.GenerateRoundUrl, new GenerateRoundRequest(MatchSide.Human("AB"), MatchSide.Human("CD"), [], "husband"));
        twoPeople.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var sameTwice = await client.PostAsJsonAsync(ApiRoutes.Watch.GenerateRoundUrl, new GenerateRoundRequest(husband, husband, [], "husband"));
        sameTwice.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var missing = await client.PostAsJsonAsync(ApiRoutes.Watch.GenerateRoundUrl, new GenerateRoundRequest(MatchSide.Persona("ZZZ"), husband, [], "husband"));
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound, "a persona that was never cast cannot argue");
    }

    [Fact]
    public async Task Re_judging_someone_elses_match_is_not_found()
    {
        using var owner = User("watch-owner");
        using var stranger = User("watch-stranger");
        var (husband, wife) = await CastAsync(owner, "06");

        var first = await owner.PostAsJsonAsync(ApiRoutes.Watch.VerdictUrl, new VerdictRequest(husband, wife, Transcript(6), "the thermostat"));
        var matchId = (await first.Content.ReadFromJsonAsync<VerdictResponse>())!.MatchId;

        var again = await owner.PostAsJsonAsync(ApiRoutes.Watch.VerdictUrl, new VerdictRequest(husband, wife, Transcript(6), "the thermostat", matchId));
        again.StatusCode.Should().Be(HttpStatusCode.OK, "the owner may re-judge their own match");
        (await again.Content.ReadFromJsonAsync<VerdictResponse>())!.MatchId.Should().Be(matchId, "re-judging replaces rather than duplicating");

        var theft = await stranger.PostAsJsonAsync(ApiRoutes.Watch.VerdictUrl, new VerdictRequest(husband, wife, Transcript(6), "the thermostat", matchId));
        theft.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_line_is_spoken_in_the_personas_voice_and_an_unknown_persona_is_not_found()
    {
        using var client = User("watch-audio");
        var (husband, _) = await CastAsync(client, "07");

        var response = await client.PostAsJsonAsync(ApiRoutes.Watch.RoundAudioUrl, new RoundAudioRequest(ProfileId.From(husband.Id), "You left the freezer open."));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var audio = (await response.Content.ReadFromJsonAsync<TtsAudioDto>())!;
        audio.IsEmpty.Should().BeFalse();
        audio.Format.Should().BeOneOf("pcm", "mp3");

        var unknown = await client.PostAsJsonAsync(ApiRoutes.Watch.RoundAudioUrl, new RoundAudioRequest(ProfileId.From("ZZZ"), "Nobody says this."));
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_recorded_turn_is_transcribed_and_a_body_that_is_not_a_recording_is_refused()
    {
        using var client = User("watch-transcribe");
        var wav = new byte[4096];
        "RIFF"u8.CopyTo(wav);
        "WAVE"u8.CopyTo(wav.AsSpan(8));

        var response = await client.PostAsJsonAsync(ApiRoutes.Watch.TranscribeUrl, new TranscribeRequest(Convert.ToBase64String(wav)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var heard = (await response.Content.ReadFromJsonAsync<TranscribeResponse>())!;
        heard.Text.Should().NotBeNullOrWhiteSpace();
        heard.IsFake.Should().BeTrue();

        var notAudio = await client.PostAsJsonAsync(ApiRoutes.Watch.TranscribeUrl, new TranscribeRequest(Convert.ToBase64String("hello there"u8.ToArray())));
        notAudio.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var notBase64 = await client.PostAsJsonAsync(ApiRoutes.Watch.TranscribeUrl, new TranscribeRequest("!!!not base64!!!"));
        notBase64.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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

    [Fact]
    public async Task A_runaway_client_is_throttled_before_it_can_spend_the_budget()
    {
        using var client = User($"watch-flood-{Guid.NewGuid():N}");
        var wav = new byte[2048];
        "RIFF"u8.CopyTo(wav);
        "WAVE"u8.CopyTo(wav.AsSpan(8));
        var body = new TranscribeRequest(Convert.ToBase64String(wav));

        var codes = new List<HttpStatusCode>();
        TimeSpan? retryAfter = null;
        for (var i = 0; i <= WatchServiceExtensions.PermitsPerWindow; i++)
        {
            using var response = await client.PostAsJsonAsync(ApiRoutes.Watch.TranscribeUrl, body);
            codes.Add(response.StatusCode);
            retryAfter ??= response.Headers.RetryAfter?.Delta;
        }

        codes.Take(WatchServiceExtensions.PermitsPerWindow).Should().AllSatisfy(c => c.Should().Be(HttpStatusCode.OK));
        codes[^1].Should().Be(HttpStatusCode.TooManyRequests, "every call here costs money at a provider, so one client cannot spend everyone's budget");
        retryAfter.Should().NotBeNull("a throttled client that is not told when to come back can only guess, and guesses badly")
            .And.BeLessThanOrEqualTo(WatchServiceExtensions.Window);

        using var other = User($"watch-calm-{Guid.NewGuid():N}");
        (await other.PostAsJsonAsync(ApiRoutes.Watch.TranscribeUrl, body)).StatusCode
            .Should().Be(HttpStatusCode.OK, "the limit is per caller, so one runaway client does not lock everyone out");
    }
}
