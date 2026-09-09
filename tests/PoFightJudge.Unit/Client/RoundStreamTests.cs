using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using PoFightJudge.Client;
using PoFightJudge.Client.Services;
using PoFightJudge.Shared;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Unit.Client;

/// <summary>
/// The two NDJSON round streams, as the client reads them. Both endpoints have existed on the server since T33 and
/// nothing ever called them: the play loop asked for a whole line and then for its whole audio, and waited out both
/// round trips. These are the readers that let it stop waiting.
/// </summary>
public class RoundStreamTests
{
    private static ApiClient Client(string body, string contentType = "application/x-ndjson")
    {
        var services = new ServiceCollection();
        foreach (var name in new[] { HttpClients.Api, HttpClients.Anonymous })
        {
            services.AddHttpClient(name, c => c.BaseAddress = new Uri("https://unit.test/"))
                .ConfigurePrimaryHttpMessageHandler(() => new CannedHandler(body, contentType));
        }

        return new ApiClient(services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>());
    }

    private static GenerateRoundRequest AnyRound() => new(
        MatchSide.Persona("MAH", "Matthew"),
        MatchSide.Persona("KSH", "Kimberly"),
        [],
        WatchTurns.Husband);

    [Fact]
    public async Task A_line_arrives_as_fragments_and_then_once_in_full()
    {
        var api = Client(
            """
            {"delta":"You "}
            {"delta":"always "}
            {"delta":"say that."}
            {"text":"You always say that.","mood":"angry","attitude":"escalating","isFake":true}
            """);

        var parts = new List<RoundStreamPart>();
        await foreach (var part in api.StreamRoundAsync(AnyRound()))
        {
            parts.Add(part);
        }

        parts.Where(p => p.Delta is not null).Select(p => p.Delta).Should().Equal("You ", "always ", "say that.");
        parts.Count(p => p.Final is not null).Should().Be(1);
        parts[^1].Final!.Text.Should().Be("You always say that.");
        parts[^1].Final!.Mood.Should().Be("angry");
    }

    /// <summary>
    /// The fragments are a courtesy; the closing object is the contract. A stream that produced only the final line
    /// — a model that answered in one chunk — is a complete stream, not a broken one.
    /// </summary>
    [Fact]
    public async Task A_line_that_arrived_all_at_once_is_still_a_line()
    {
        var api = Client("""{"text":"Fine.","mood":"cold","attitude":"flat","isFake":false}""");

        var parts = new List<RoundStreamPart>();
        await foreach (var part in api.StreamRoundAsync(AnyRound()))
        {
            parts.Add(part);
        }

        parts.Should().ContainSingle();
        parts[0].Final!.Text.Should().Be("Fine.");
    }

    /// <summary>A blank line in an NDJSON body is whitespace, not an item — and a half-written trailing line is not one either.</summary>
    [Fact]
    public async Task Blank_and_unreadable_lines_are_skipped_rather_than_thrown()
    {
        var api = Client("\n{\"delta\":\"Hi\"}\n\n{ not json\n{\"text\":\"Hi\",\"mood\":\"calm\",\"attitude\":\"flat\",\"isFake\":true}\n");

        var parts = new List<RoundStreamPart>();
        await foreach (var part in api.StreamRoundAsync(AnyRound()))
        {
            parts.Add(part);
        }

        parts.Should().HaveCount(2);
        parts[0].Delta.Should().Be("Hi");
        parts[1].Final!.Text.Should().Be("Hi");
    }

    [Fact]
    public async Task Audio_arrives_clause_by_clause_with_the_format_each_one_came_back_in()
    {
        var api = Client(
            """
            {"index":0,"base64":"AAA=","format":"pcm","isLast":false}
            {"index":1,"base64":"BBB=","format":"mp3","isLast":true}
            """);

        var chunks = new List<RoundAudioChunkDto>();
        await foreach (var chunk in api.StreamRoundAudioAsync(new RoundAudioRequest(ProfileId.From("MAH"), "You always say that.")))
        {
            chunks.Add(chunk);
        }

        chunks.Should().HaveCount(2);
        chunks[0].Index.Should().Be(0);
        chunks[0].Audio.Should().Be(new TtsAudioDto("AAA=", "pcm"));
        chunks[1].IsLast.Should().BeTrue();
        chunks[1].Audio.Format.Should().Be("mp3", "the chain can fall back mid-line, so the format travels per chunk");
    }

    /// <summary>A voiceless line is still a line: the stream closes and the argument carries on.</summary>
    [Fact]
    public async Task An_empty_audio_stream_yields_nothing_and_does_not_throw()
    {
        var api = Client(string.Empty);

        var chunks = new List<RoundAudioChunkDto>();
        await foreach (var chunk in api.StreamRoundAudioAsync(new RoundAudioRequest(ProfileId.From("MAH"), "…")))
        {
            chunks.Add(chunk);
        }

        chunks.Should().BeEmpty();
    }

    [Fact]
    public async Task A_refused_stream_surfaces_as_the_same_ApiException_every_other_call_throws()
    {
        var services = new ServiceCollection();
        services.AddHttpClient(HttpClients.Api, c => c.BaseAddress = new Uri("https://unit.test/"))
            .ConfigurePrimaryHttpMessageHandler(() => new CannedHandler(string.Empty, "application/json", HttpStatusCode.NotFound));
        services.AddHttpClient(HttpClients.Anonymous, c => c.BaseAddress = new Uri("https://unit.test/"));
        var api = new ApiClient(services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>());

        var read = async () =>
        {
            await foreach (var _ in api.StreamRoundAsync(AnyRound()))
            {
            }
        };

        await read.Should().ThrowAsync<ApiException>();
    }

    private sealed class CannedHandler(string body, string contentType, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, contentType),
            });
    }
}

/// <summary>
/// Streamed audio arrives as clauses and is archived as one clip, because a replay plays a line back rather than a
/// phrase of it.
/// </summary>
public class TtsAudioJoinTests
{
    [Fact]
    public void Clauses_in_one_format_join_into_one_clip()
    {
        var first = Convert.ToBase64String([1, 2, 3]);
        var second = Convert.ToBase64String([4, 5]);

        var joined = TtsAudioDto.Join([new TtsAudioDto(first, "pcm"), new TtsAudioDto(second, "pcm")]);

        joined.Should().NotBeNull();
        joined!.Format.Should().Be("pcm");
        Convert.FromBase64String(joined.Base64).Should().Equal(1, 2, 3, 4, 5);
    }

    /// <summary>The bug this guards: two padded base64 strings concatenated are not the base64 of the two payloads.</summary>
    [Fact]
    public void The_bytes_are_joined_rather_than_the_base64()
    {
        var padded = Convert.ToBase64String([9]);
        padded.Should().EndWith("==", "a one-byte payload is padded, which is what makes naive concatenation wrong");

        var joined = TtsAudioDto.Join([new TtsAudioDto(padded, "pcm"), new TtsAudioDto(padded, "pcm")]);

        Convert.FromBase64String(joined!.Base64).Should().Equal(9, 9);
    }

    [Fact]
    public void A_line_that_fell_back_mid_way_archives_nothing_rather_than_a_clip_in_two_formats()
    {
        TtsAudioDto.Join([new TtsAudioDto("AAA=", "pcm"), new TtsAudioDto("BBB=", "mp3")]).Should().BeNull();
    }

    [Fact]
    public void Silence_joins_to_nothing()
    {
        TtsAudioDto.Join([]).Should().BeNull();
        TtsAudioDto.Join([TtsAudioDto.None]).Should().BeNull();
    }

    /// <summary>An empty clause between two spoken ones is a clause the chain could not say, not a gap in the clip.</summary>
    [Fact]
    public void An_unsayable_clause_is_left_out_rather_than_taken_as_a_format()
    {
        var joined = TtsAudioDto.Join([new TtsAudioDto("AAA=", "pcm"), TtsAudioDto.None, new TtsAudioDto("BBB=", "pcm")]);

        joined.Should().NotBeNull();
        joined!.Format.Should().Be("pcm");
    }
}
