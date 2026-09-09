using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PoFightJudge.Api.Features.Ai;
using PoFightJudge.Api.Features.Ai.Fakes;
using PoFightJudge.Api.Features.Diagnostics;
using PoFightJudge.Api.Features.Voice;
using PoFightJudge.TestSupport;

namespace PoFightJudge.Unit.Voice;

public class TranscriptsTests
{
    [Theory]
    [InlineData("Fine, whatever.", "Fine, whatever.")]
    [InlineData("  Transcript: I did not say that.  ", "I did not say that.")]
    [InlineData("\"You always do this.\"", "You always do this.")]
    [InlineData("```\nThe dishes were done.\n```", "The dishes were done.")]
    [InlineData("NO_SPEECH", "")]
    [InlineData("[inaudible]", "")]
    [InlineData("(silence)", "")]
    [InlineData("   ", "")]
    public void Clean_strips_wrappers_and_turns_narrated_silence_into_an_empty_transcript(string raw, string expected) =>
        Transcripts.Clean(raw).Should().Be(expected);

    [Fact]
    public void A_real_line_about_silence_survives_because_the_marker_check_is_length_capped()
    {
        const string line = "Do not give me the silence treatment again, because that is exactly what you did last time and I am done pretending it is fine.";

        Transcripts.Clean(line).Should().Be(line);
        Transcripts.IsNonSpeech(line).Should().BeFalse();
    }

    [Fact]
    public void Only_a_riff_wave_container_looks_like_a_wav()
    {
        var wav = new byte[64];
        "RIFF"u8.CopyTo(wav);
        "WAVE"u8.CopyTo(wav.AsSpan(8));

        Transcripts.LooksLikeWav(wav).Should().BeTrue();
        Transcripts.LooksLikeWav("not audio at all"u8).Should().BeFalse();
        Transcripts.LooksLikeWav([]).Should().BeFalse();
        Transcripts.MaxWavBytes.Should().Be(4 * 1024 * 1024);
    }
}

public class FakeTranscriptionTests
{
    private static byte[] Wav(int length)
    {
        var wav = new byte[length];
        "RIFF"u8.CopyTo(wav);
        "WAVE"u8.CopyTo(wav.AsSpan(8));
        return wav;
    }

    [Fact]
    public async Task A_real_clip_gets_a_canned_line_that_says_it_is_fake_and_a_dud_clip_gets_nothing()
    {
        var sut = new FakeTranscription(NullLogger<FakeTranscription>.Instance);

        var line = await sut.TranscribeAsync(Wav(8192));

        line.Should().Contain("(fake transcript)", "a dead transcriber must never look like a bad microphone");
        (await sut.TranscribeAsync(Wav(8192))).Should().Be(line, "the same clip transcribes the same way");
        (await sut.TranscribeAsync(Wav(100))).Should().BeEmpty("too short to be a turn");
        (await sut.TranscribeAsync(Encoding.UTF8.GetBytes(new string('x', 9000)))).Should().BeEmpty("not a WAV container");
        (await sut.TranscribeAsync([])).Should().BeEmpty();
        sut.IsEnabled.Should().BeTrue();
        sut.Name.Should().Be("fake");
    }
}

public class GeminiTranscriptionServiceTests
{
    private readonly FakeHttpHandler _http = new();

    private GeminiTranscriptionService Sut(bool hasKey = true)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(GeminiHttpClients.Fast).Returns(_ => _http.CreateClient());
        return new GeminiTranscriptionService(factory, GeminiModelOptions.Defaults, new AiMode(false, hasKey, "test"), new AiLatencyTracker(), NullLogger<GeminiTranscriptionService>.Instance);
    }

    [Fact]
    public async Task Sends_the_clip_as_an_inline_audio_part_with_the_transcriber_instruction()
    {
        _http.Enqueue(HttpStatusCode.OK, """{"candidates":[{"content":{"parts":[{"text":"Transcript: I said what I said."}]}}]}""");
        var wav = new byte[64];
        "RIFF"u8.CopyTo(wav);
        "WAVE"u8.CopyTo(wav.AsSpan(8));

        var text = await Sut().TranscribeAsync(wav);

        text.Should().Be("I said what I said.", "the label is stripped");
        var sent = _http.Requests.Should().ContainSingle().Which;
        sent.Uri!.ToString().Should().EndWith($"v1beta/models/{GeminiModelOptions.Defaults.Transcribe}:generateContent");
        var body = JsonNode.Parse(sent.Body!)!;
        var parts = body["contents"]![0]!["parts"]!.AsArray();
        parts[0]!["text"]!.GetValue<string>().Should().Contain("You are not a participant.").And.Contain(Transcripts.NoSpeechSentinel);
        parts[1]!["inlineData"]!["mimeType"]!.GetValue<string>().Should().Be("audio/wav");
        Convert.FromBase64String(parts[1]!["inlineData"]!["data"]!.GetValue<string>()).Should().Equal(wav);
        body["generationConfig"]!["temperature"]!.GetValue<int>().Should().Be(0, "transcription is a reading task, not a creative one");
    }

    [Fact]
    public async Task The_no_speech_sentinel_becomes_an_empty_line_and_errors_surface()
    {
        var wav = new byte[64];
        "RIFF"u8.CopyTo(wav);
        "WAVE"u8.CopyTo(wav.AsSpan(8));

        _http.Enqueue(HttpStatusCode.OK, """{"candidates":[{"content":{"parts":[{"text":"NO_SPEECH"}]}}]}""".Replace("NO_SPEECH", Transcripts.NoSpeechSentinel, StringComparison.Ordinal));
        (await Sut().TranscribeAsync(wav)).Should().BeEmpty();

        _http.Enqueue(HttpStatusCode.BadRequest, """{"error":{"message":"unsupported audio"}}""");
        var act = () => Sut().TranscribeAsync(wav);
        (await act.Should().ThrowAsync<HttpRequestException>()).Which.Message.Should().Contain("unsupported audio");

        Sut(hasKey: false).IsEnabled.Should().BeFalse("without a key the SELF turn cannot finish");
        Sut().Name.Should().Be("gemini");
    }
}
