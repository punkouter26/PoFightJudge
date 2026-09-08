using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using PoMarriedFight.Api.Features.Auth;
using PoMarriedFight.Api.Features.Fighters;
using PoMarriedFight.Api.Features.Records;
using PoMarriedFight.Api.Features.Storage;
using PoMarriedFight.Shared;
using PoMarriedFight.Shared.Identifiers;
using PoMarriedFight.Shared.Models;

namespace PoMarriedFight.E2EAPI;

/// <summary>
/// Reading a fight back over HTTP. The offline stand-ins do the reading, so a whole fight goes from ended to
/// readable with no key.
/// </summary>
[Collection(ApiCollection.Name)]
public class AnalysisTests(ApiFactory factory)
{
    private HttpClient User(string id)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(FakeAuthOptions.UserHeader, id);
        return client;
    }

    /// <summary>Starts a fight, lets it end, and waits for it to have been read.</summary>
    private static async Task<MatchId> FoughtAsync(HttpClient client, string one, string two)
    {
        var started = await client.PostAsJsonAsync(ApiRoutes.Fights.Base, new CreateFightRequest(HostPersonaId.Referee, one, two, "the thermostat"));
        started.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await started.Content.ReadFromJsonAsync<CreateFightResponse>())!.MatchId;
        await client.PostAsync(ApiRoutes.Fights.End(id), null);
        return id;
    }

    private static async Task<AnalysisResponse> WaitForAsync(HttpClient client, MatchId id, AnalysisStatus wanted)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var response = await client.GetAsync(ApiRoutes.Fights.Analysis(id));
            var body = await response.Content.ReadFromJsonAsync<AnalysisResponse>();
            if (body?.Status == wanted)
            {
                return body;
            }

            await Task.Delay(25);
        }

        throw new InvalidOperationException($"The analysis never reached {wanted}.");
    }

    [Fact]
    public async Task A_fight_that_recorded_nothing_says_so_rather_than_waiting_forever()
    {
        var client = User("analysis-empty");

        // Nobody spoke into it, so there is no recording to read and never will be.
        var id = await FoughtAsync(client, "n1", "n2");

        var response = await client.GetAsync(ApiRoutes.Fights.Analysis(id));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AnalysisResponse>();
        body!.Status.Should().Be(AnalysisStatus.Failed);
        body.Error.Should().Contain("Nothing was recorded");
    }

    [Fact]
    public async Task Somebody_elses_fight_cannot_be_read()
    {
        var mine = User("analysis-owner");
        var theirs = User("analysis-stranger");
        var id = await FoughtAsync(mine, "o1", "o2");

        (await theirs.GetAsync(ApiRoutes.Fights.Analysis(id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await theirs.PostAsync(ApiRoutes.Fights.AnalysisRetry(id), null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await theirs.GetAsync(ApiRoutes.Fights.Clip(id, 0))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_fight_that_never_happened_is_not_found()
    {
        var client = User("analysis-missing");
        var id = MatchId.New();

        (await client.GetAsync(ApiRoutes.Fights.Analysis(id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync(ApiRoutes.Fights.Clip(id, 0))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Retrying_a_fight_with_no_recording_is_refused_with_a_reason()
    {
        var client = User("analysis-retry-empty");
        var id = await FoughtAsync(client, "r1", "r2");

        var retry = await client.PostAsync(ApiRoutes.Fights.AnalysisRetry(id), null);

        retry.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await retry.Content.ReadAsStringAsync()).Should().Contain("Nothing was recorded");
    }

    [Fact]
    public async Task A_clip_of_a_fight_that_was_never_read_is_not_found()
    {
        var client = User("analysis-clip");
        var id = await FoughtAsync(client, "c1", "c2");

        (await client.GetAsync(ApiRoutes.Fights.Clip(id, 0))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync(ApiRoutes.Fights.Clip(id, -1))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_transcript_from_the_browser_is_only_taken_when_that_is_switched_on()
    {
        var client = User("analysis-transcript");
        var id = await FoughtAsync(client, "t1", "t2");

        var posted = await client.PostAsJsonAsync(
            ApiRoutes.Fights.Transcript(id),
            new TranscriptDto("you never listen", [new TranscriptWord("you", "spk_1", 0, 0.4)]));

        // Off by default: the server model stays the source of truth until the browser path is proven.
        posted.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reading_a_fight_needs_signing_in()
    {
        var anonymous = factory.CreateClient();
        var id = MatchId.New();

        (await anonymous.GetAsync(ApiRoutes.Fights.Analysis(id))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsync(ApiRoutes.Fights.AnalysisRetry(id), null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_fight_somebody_actually_argued_is_read_and_both_of_them_get_a_record()
    {
        var client = User("analysis-full");
        var id = await FoughtAsync(client, "f1", "f2");

        // The room is what feeds a fight; there is no room here, so the recording is put in by hand and re-read.
        await GiveItARecordingAsync(id);
        var retry = await client.PostAsync(ApiRoutes.Fights.AnalysisRetry(id), null);
        retry.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var ready = await WaitForAsync(client, id, AnalysisStatus.Ready);
        ready.Report.Should().NotBeNull();
        ready.Report!.MatchId.Should().Be(id);
        ready.Report.Player1.Metrics.Words.Should().BeGreaterThan(0, "the stand-in transcriber says something");
        ready.Report.Overall.Reasons.Should().NotBeEmpty();

        var results = factory.Services.GetRequiredService<IFighterResultRepository>();
        (await results.ListForAsync(FighterId.From("F1"), "analysis-full")).Should().ContainSingle();
        (await results.ListForAsync(FighterId.From("F2"), "analysis-full")).Should().ContainSingle();

        // And both of them are now somebody the CPU and 1P channels can put in a seat, read from this fight. The
        // persona is written after the ruling is readable, deliberately — the room is not kept waiting on it — so
        // it is allowed a moment to land.
        var f1 = await WaitForPersonaAsync(client, "F1");
        f1.FromFights.Should().BeTrue();
        f1.Persona.Role.Should().Be(ProfileRole.Husband, "fighter one sits as the husband unless setup said otherwise");
        (await WaitForPersonaAsync(client, "F2")).Persona.Role.Should().Be(ProfileRole.Wife);
    }

    private static async Task<ProfileDto> WaitForPersonaAsync(HttpClient client, string initials)
    {
        const int Attempts = 100; // a hundred looks, 100 ms apart: ten seconds for a stand-in model that answers at once
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var cast = await client.GetFromJsonAsync<List<ProfileDto>>(ApiRoutes.Profiles.Base) ?? [];
            if (cast.Find(p => string.Equals(p.Persona.Initials, initials, StringComparison.Ordinal)) is { } persona)
            {
                return persona;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"No persona for {initials} appeared in the cast within 10s.");
    }

    /// <summary>Puts a recording where the pipeline expects one, for a fight nobody spoke into.</summary>
    private async Task GiveItARecordingAsync(MatchId id)
    {
        var matches = factory.Services.GetRequiredService<IMatchRepository>();
        var blobs = factory.Services.GetRequiredService<IAudioBlobStore>();
        var name = IAudioBlobStore.PlayersTrack(id);

        using var wav = new MemoryStream(PoMarriedFight.Api.Features.Fight.WavWriter.Build(16_000, new byte[32_000]));
        await blobs.UploadAsync(name, wav, "audio/wav");

        var match = await matches.GetAsync("analysis-full", id) ?? throw new InvalidOperationException("The fight vanished.");
        await matches.UpsertAsync(match with { AudioBlobName = name });
    }
}
