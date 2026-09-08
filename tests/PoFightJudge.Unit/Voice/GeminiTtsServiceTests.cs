using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PoFightJudge.Api.Features.Ai;
using PoFightJudge.Api.Features.Ai.Fakes;
using PoFightJudge.Api.Features.Diagnostics;
using PoFightJudge.Api.Features.Profiles;
using PoFightJudge.Api.Features.Voice;
using PoFightJudge.TestSupport;

namespace PoFightJudge.Unit.Voice;

public class GeminiTtsServiceTests
{
    private readonly FakeHttpHandler _http = new();
    private readonly AiLatencyTracker _latency = new();
    private readonly GeminiTtsService _sut;

    public GeminiTtsServiceTests()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(GeminiHttpClients.Tts).Returns(_ => _http.CreateClient());
        _sut = new GeminiTtsService(factory, GeminiModelOptions.Defaults, _latency, TimeProvider.System, NullLogger<GeminiTtsService>.Instance);
    }

    [Fact]
    public async Task Posts_the_documented_interactions_request_and_passes_the_pcm_through()
    {
        _http.Enqueue(HttpStatusCode.OK, """{"id":"interactions/abc","status":"completed","output_audio":{"data":"UENN"}}""");

        var audio = await _sut.SynthesizeAsync("I did the dishes.", new TtsSettings(1.0, 1.0, "Kore"));

        audio.Should().Be(TtsAudio.Pcm("UENN"));
        var sent = _http.Requests.Should().ContainSingle().Which;
        sent.Uri!.AbsolutePath.Should().EndWith("/v1beta/interactions");
        var body = JsonNode.Parse(sent.Body!)!;
        body["model"]!.GetValue<string>().Should().Be("gemini-3.1-flash-tts-preview");
        body["input"]!.GetValue<string>().Should().Be("I did the dishes.", "neutral prosody adds no prefix");
        body["response_format"]!["type"]!.GetValue<string>().Should().Be("audio");
        body["generation_config"]!["speech_config"]![0]!["voice"]!.GetValue<string>().Should().Be("Kore");
        _latency.Snapshot().Should().ContainSingle(s => string.Equals(s.Operation, GeminiTtsService.Operation, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_pending_interaction_is_polled_by_id_until_it_completes()
    {
        _http.Enqueue(HttpStatusCode.OK, """{"id":"interactions/p1","status":"in_progress"}""");
        _http.Enqueue(HttpStatusCode.OK, """{"id":"interactions/p1","status":"in_progress"}""");
        _http.Enqueue(HttpStatusCode.OK, """{"id":"interactions/p1","status":"completed","outputs":[{"type":"audio","data":"QUJD","mime_type":"audio/pcm"}]}""");

        var audio = await _sut.SynthesizeAsync("Hello there.", new TtsSettings(1.0, 1.0, "Puck"));

        audio.Base64.Should().Be("QUJD");
        _http.Requests.Should().HaveCount(3);
        _http.Requests[1].Method.Should().Be(HttpMethod.Get);
        _http.Requests[1].Uri!.AbsolutePath.Should().EndWith("/v1beta/interactions%2Fp1");
    }

    [Fact]
    public async Task Provider_errors_and_failed_interactions_surface_as_exceptions_and_no_audio_is_none()
    {
        _http.Enqueue(HttpStatusCode.TooManyRequests, """{"error":{"message":"quota"}}""");
        var throttled = () => _sut.SynthesizeAsync("x", new TtsSettings(1, 1, "Kore"));
        (await throttled.Should().ThrowAsync<HttpRequestException>()).Which.Message.Should().Contain("quota");

        _http.Enqueue(HttpStatusCode.OK, """{"id":"i","status":"failed","error":{"message":"voice unavailable"}}""");
        var failed = () => _sut.SynthesizeAsync("x", new TtsSettings(1, 1, "Kore"));
        (await failed.Should().ThrowAsync<HttpRequestException>()).Which.Message.Should().Contain("voice unavailable");

        GeminiTtsService.ExtractAudio(JsonNode.Parse("""{"status":"completed","outputs":[{"type":"text","text":"no audio"}]}""") as JsonObject).Should().Be(TtsAudio.None);
        GeminiTtsService.ExtractAudio(JsonNode.Parse("""{"steps":[{"type":"model_output","content":[{"type":"audio","data":"U1RFUA=="}]}]}""") as JsonObject).Base64.Should().Be("U1RFUA==");
    }

    [Theory]
    [InlineData(1.0, 1.0, "Hi.")]
    [InlineData(0.8, 1.0, "Say in a low, deep voice: Hi.")]
    [InlineData(1.3, 1.3, "Say in a high-pitched voice, quickly: Hi.")]
    [InlineData(1.0, 0.85, "Say slowly and deliberately: Hi.")]
    public void Sliders_become_a_spoken_direction_prefix(double pitch, double speed, string expected) =>
        GeminiTtsService.ApplyVoiceStyle("Hi.", new TtsSettings(pitch, speed, "Kore")).Should().Be(expected);
}

public class FakeTtsTests
{
    private const string LongLine = "You left the freezer open all night and everything in it is ruined.";

    [Fact]
    public async Task Duration_tracks_the_word_count_and_the_bytes_are_valid_24khz_pcm()
    {
        var sut = new FakeTts();

        var shortLine = await sut.SynthesizeAsync("Fine.", new TtsSettings(1, 1, "Kore"));
        var longLine = await sut.SynthesizeAsync(LongLine, new TtsSettings(0.7, 1, "Charon"));

        var shortBytes = Convert.FromBase64String(shortLine.Base64);
        var longBytes = Convert.FromBase64String(longLine.Base64);
        shortLine.Format.Should().Be(TtsAudioFormats.Pcm);
        (shortBytes.Length % 2).Should().Be(0, "16-bit samples");
        shortBytes.Length.Should().Be((int)(FakeTts.DurationFor("Fine.").TotalSeconds * TtsAudioFormats.PcmSampleRate) * 2);
        longBytes.Length.Should().Be((int)(FakeTts.DurationFor(LongLine).TotalSeconds * TtsAudioFormats.PcmSampleRate) * 2);
        longBytes.Length.Should().BeGreaterThan(shortBytes.Length * 4, "thirteen words take far longer to say than one");
        FakeTts.DurationFor("one two three").Should().Be(TimeSpan.FromMilliseconds(FakeTts.BreathMilliseconds + (3 * FakeTts.MillisecondsPerWord)));
        sut.IsFake.Should().BeTrue();
        longBytes.Skip(2000).Take(2000).Should().Contain(b => b != 0, "there is an audible tone, not silence");
    }
}
