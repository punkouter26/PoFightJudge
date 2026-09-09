using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using PoFightJudge.Api.Features.Diagnostics;
using PoFightJudge.Api.Features.Voice;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Api.Features.Analysis;

/// <summary>
/// Reads a whole finished fight with Azure AI Speech fast transcription: two speakers, word-level offsets.
/// </summary>
/// <remarks>
/// The fallback this replaces is <c>gemini-3.5-transcribe</c>, and
/// <see cref="Voice.GeminiTranscriptionService"/>'s own remarks record what it does to a clip: accepts the request,
/// bills the audio tokens, answers 200 with <c>finishReason: STOP</c>, and returns an empty part. AGENT.md has the
/// fight fallback as never having run against the real service, so the one path that costs money is also the one
/// nobody has watched work.
///
/// Azure is already configured here — it transcribes WATCH turns — diarizes to a stated speaker count and returns
/// word offsets, which is the whole contract <see cref="SpeakerMapper"/> needs. Its limits are 500 MB and five
/// hours; a fifteen-minute fight is 28 MB.
/// </remarks>
public sealed partial class AzureDiarizedTranscriber(
    IHttpClientFactory httpClients,
    AzureSpeechOptions options,
    AiLatencyTracker latency,
    ILogger<AzureDiarizedTranscriber> logger) : IRecordingTranscriber
{
    public const string Operation = "transcribe.fight";

    /// <summary>
    /// Pinned, and deliberately not the one <see cref="Voice.AzureTranscriptionService"/> uses. That one was read
    /// against <c>combinedPhrases</c> alone; this needs <c>phrases[].words</c>, which is the shape this version
    /// documents. Moving either is a re-read of the contract, not a bump.
    /// </summary>
    public const string ApiVersion = "2025-10-15";

    /// <summary>A fight is two people. Saying so beats letting the service decide how many it heard.</summary>
    public const int Speakers = 2;

    public string Name => "azure-diarized";

    public async Task<TranscriptDto> TranscribeAsync(RecordingRef recording, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(recording);

        await using var audio = await recording.OpenAsync(ct);
        using var buffer = new MemoryStream();
        await audio.Content.CopyToAsync(buffer, ct);

        using var content = new MultipartFormDataContent();
        using var clip = new ByteArrayContent(buffer.GetBuffer(), 0, (int)buffer.Length);
        clip.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(clip, "audio", "fight.wav");

        using var definition = new StringContent(BuildDefinition());
        definition.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        content.Add(definition, "definition");

        var url = new Uri($"https://{options.Region}.api.cognitive.microsoft.com/speechtotext/transcriptions:transcribe?api-version={ApiVersion}");
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        request.Headers.Add("Ocp-Apim-Subscription-Key", options.Key);

        var client = httpClients.CreateClient(AzureSpeechService.ClientName);
        var started = Stopwatch.GetTimestamp();
        using var response = await client.SendAsync(request, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);
        var elapsed = Stopwatch.GetElapsedTime(started);
        latency.Record(Operation, elapsed.TotalMilliseconds);
        LogCompleted(logger, buffer.Length, elapsed.TotalMilliseconds, (int)response.StatusCode);

        if (!response.IsSuccessStatusCode)
        {
            var detail = raw.Length > 300 ? raw[..300] : raw;
            LogFailed(logger, (int)response.StatusCode, detail);
            throw new HttpRequestException($"Azure fight transcription returned {(int)response.StatusCode}: {detail}", null, response.StatusCode);
        }

        return ParseResponse(raw);
    }

    /// <summary>What to ask for. Profanity is left alone: an argument is judged on what was said.</summary>
    public static string BuildDefinition() => new JsonObject
    {
        ["locales"] = new JsonArray("en-US"),
        ["diarization"] = new JsonObject { ["enabled"] = true, ["maxSpeakers"] = Speakers },
        ["profanityFilterMode"] = "None",
    }.ToJsonString();

    /// <summary>
    /// The phrases, flattened to words. Offsets arrive in milliseconds and the transcript is in seconds; the
    /// speaker rides on the phrase rather than on each word, so its words inherit it.
    /// </summary>
    public static TranscriptDto ParseResponse(string rawJson)
    {
        JsonObject? root;
        try
        {
            root = JsonNode.Parse(rawJson) as JsonObject;
        }
        catch (JsonException)
        {
            // A shape this does not recognise is an empty transcript, not an exception: the pipeline reads an empty
            // one as "too little was said to judge", which is a ruling, where a throw loses the whole report.
            return new TranscriptDto(string.Empty, []);
        }

        if (root?["phrases"] is not JsonArray phrases)
        {
            return new TranscriptDto(string.Empty, []);
        }

        var words = new List<TranscriptWord>();
        var text = new List<string>();

        foreach (var phrase in phrases.OfType<JsonObject>())
        {
            var label = $"spk_{Int(phrase, "speaker", fallback: 1)}";
            var start = Int(phrase, "offsetMilliseconds", 0) / 1000.0;
            var length = Int(phrase, "durationMilliseconds", 0) / 1000.0;
            var spoken = phrase["text"]?.GetValue<string>() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(spoken))
            {
                text.Add(spoken.Trim());
            }

            if (phrase["words"] is JsonArray timed && timed.Count > 0)
            {
                foreach (var word in timed.OfType<JsonObject>())
                {
                    if (word["text"]?.GetValue<string>() is not { Length: > 0 } value)
                    {
                        continue;
                    }

                    var at = Int(word, "offsetMilliseconds", 0) / 1000.0;
                    words.Add(new TranscriptWord(value, label, at, at + (Int(word, "durationMilliseconds", 0) / 1000.0)));
                }

                continue;
            }

            // No word timings on this phrase. The mapper needs offsets, so they are spread across the phrase's own
            // span — the same estimate the browser's recogniser produces, and honest for the same reason.
            var parts = spoken.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var each = parts.Length == 0 ? 0 : length / parts.Length;
            for (var i = 0; i < parts.Length; i++)
            {
                words.Add(new TranscriptWord(parts[i], label, start + (each * i), start + (each * (i + 1))));
            }
        }

        return new TranscriptDto(string.Join(' ', text), words);

        static int Int(JsonObject node, string key, int fallback) =>
            node[key] is JsonValue v && v.TryGetValue<int>(out var n) ? n : fallback;
    }

    [LoggerMessage(EventId = 6200, Level = LogLevel.Information, Message = "Fight transcribed by Azure: {Bytes} bytes in {ElapsedMs:F0}ms (HTTP {Status})")]
    private static partial void LogCompleted(ILogger logger, long bytes, double elapsedMs, int status);

    [LoggerMessage(EventId = 6201, Level = LogLevel.Error, Message = "Azure fight transcription was rejected (HTTP {Status}): {Detail}")]
    private static partial void LogFailed(ILogger logger, int status, string detail);
}
