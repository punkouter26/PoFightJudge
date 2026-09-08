using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PoFightJudge.Api.Features.Diagnostics;
using PoFightJudge.Api.Features.Profiles;
using PoFightJudge.Api.Features.Voice;
using PoFightJudge.TestSupport;

namespace PoFightJudge.Unit.Voice;

public class FishAudioServiceTests
{
    private readonly FakeHttpHandler _http = new();

    private FishAudioService Sut(string? defaultReference = null, string format = TtsAudioFormats.Mp3)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(FishAudioService.ClientName).Returns(_ => _http.CreateClient("https://api.fish.audio/"));
        return new FishAudioService(factory, new FishAudioOptions(true, defaultReference), new TtsRoutingOptions(true, format, false), new AiLatencyTracker(), NullLogger<FishAudioService>.Instance);
    }

    [Fact]
    public async Task Posts_the_persona_voice_with_the_sample_rate_the_format_requires()
    {
        _http.Enqueue((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3, 4]) });

        var audio = await Sut().SynthesizeAsync("Fine.", new TtsSettings(1, 1, "Kore", "persona-voice"));

        audio.Format.Should().Be(TtsAudioFormats.Mp3);
        Convert.FromBase64String(audio.Base64).Should().Equal([1, 2, 3, 4]);
        var sent = _http.Requests.Should().ContainSingle().Which;
        sent.Uri.Should().Be(FishAudioService.Endpoint);
        sent.Headers["model"].Should().Be(FishAudioService.Model);
        var body = JsonNode.Parse(sent.Body!)!;
        body["reference_id"]!.GetValue<string>().Should().Be("persona-voice");
        body["format"]!.GetValue<string>().Should().Be("mp3");
        body["sample_rate"]!.GetValue<int>().Should().Be(FishAudioService.Mp3SampleRate, "Fish rejects any other rate for mp3 with a 400");
        FishAudioService.SampleRateFor(TtsAudioFormats.Pcm).Should().Be(FishAudioService.PcmSampleRate);
    }

    [Fact]
    public async Task The_shared_default_covers_a_persona_without_its_own_voice_and_errors_carry_the_body()
    {
        var sut = Sut(defaultReference: "shared-voice");
        sut.CanSpeak(new TtsSettings(1, 1, "Kore")).Should().BeTrue();
        sut.ReferenceIdFor(new TtsSettings(1, 1, "Kore", "own")).Should().Be("own", "a persona voice always wins");
        Sut().CanSpeak(new TtsSettings(1, 1, "Kore")).Should().BeFalse("no persona voice and no shared default");

        _http.Enqueue((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) });
        (await sut.SynthesizeAsync("Fine.", new TtsSettings(1, 1, "Kore"))).Should().Be(TtsAudio.None, "an empty body is silence, not audio");

        _http.Enqueue(HttpStatusCode.PaymentRequired, "credit required");
        var act = () => sut.SynthesizeAsync("Fine.", new TtsSettings(1, 1, "Kore"));
        (await act.Should().ThrowAsync<HttpRequestException>()).Which.Message.Should().Contain("402").And.Contain("credit required");
    }
}

public class AzureSpeechServiceTests
{
    private readonly FakeHttpHandler _http = new();

    private AzureSpeechService Sut(bool enabled = true, string format = TtsAudioFormats.Mp3)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(AzureSpeechService.ClientName).Returns(_ => _http.CreateClient("https://eastus.tts.speech.microsoft.com/"));
        return new AzureSpeechService(factory, new AzureSpeechOptions(enabled, "secret-key", "eastus"), new TtsRoutingOptions(true, format, false), new AiLatencyTracker(), NullLogger<AzureSpeechService>.Instance);
    }

    [Fact]
    public async Task Sends_ssml_to_the_regional_endpoint_with_the_headers_the_service_demands()
    {
        _http.Enqueue((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([9, 9]) });

        var audio = await Sut().SynthesizeAsync("Say <this> & that.", new TtsSettings(1.0, 1.0, "Kore"));

        audio.Format.Should().Be(TtsAudioFormats.Mp3);
        var sent = _http.Requests.Should().ContainSingle().Which;
        sent.Uri!.ToString().Should().Be("https://eastus.tts.speech.microsoft.com/cognitiveservices/v1");
        sent.Headers["Ocp-Apim-Subscription-Key"].Should().Be("secret-key");
        sent.Headers["X-Microsoft-OutputFormat"].Should().Be("audio-24khz-48kbitrate-mono-mp3");
        sent.Headers["User-Agent"].Should().Be(AzureSpeechService.UserAgent, "the endpoint answers 400 with an empty body when it is missing");
        sent.Body.Should().Contain("en-US-JennyNeural").And.Contain("Say &lt;this&gt; &amp; that.");
        AzureSpeechService.OutputFormatHeader(TtsAudioFormats.Pcm).Should().Be("raw-24khz-16bit-mono-pcm");
    }

    [Theory]
    [InlineData(1.0, 1.0, "+0%", "+0%")]
    [InlineData(1.4, 1.3, "+10%", "+12%")]
    [InlineData(0.6, 0.85, "-10%", "-6%")]
    [InlineData(3.0, 3.0, "+12%", "+15%")]
    public void Sliders_are_damped_and_clamped_into_a_human_range(double pitch, double speed, string expectedPitch, string expectedRate)
    {
        var ssml = AzureSpeechService.BuildSsml("Hi.", new TtsSettings(pitch, speed, "Charon"));

        ssml.Should().Contain($"pitch='{expectedPitch}'").And.Contain($"rate='{expectedRate}'");
    }

    [Fact]
    public async Task A_rejection_quotes_the_body_and_an_unconfigured_service_refuses_up_front()
    {
        _http.Enqueue((_, _) => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes("Invalid voice name")) });
        var act = () => Sut().SynthesizeAsync("Hi.", new TtsSettings(1, 1, "Kore"));
        (await act.Should().ThrowAsync<HttpRequestException>()).Which.Message.Should().Contain("Invalid voice name");

        var off = Sut(enabled: false);
        off.CanSpeak(new TtsSettings(1, 1, "Kore")).Should().BeFalse();
        var unconfigured = () => off.SynthesizeAsync("Hi.", new TtsSettings(1, 1, "Kore"));
        await unconfigured.Should().ThrowAsync<InvalidOperationException>();
    }
}
