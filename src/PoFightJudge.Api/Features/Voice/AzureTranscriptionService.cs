using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using PoFightJudge.Api.Features.Diagnostics;

namespace PoFightJudge.Api.Features.Voice;

/// <summary>
/// Azure AI Speech "fast transcription" for a live human turn, preferred whenever a Speech key and region are
/// configured. A SELF turn is the only place in the app where a person sits still and waits with nothing on screen
/// changing: this purpose-built batch endpoint returns a short clip in well under a second, where routing the same
/// clip through a general multimodal model costs several. It also reuses the credentials Azure Speech TTS needs.
/// </summary>
public sealed partial class AzureTranscriptionService(IHttpClientFactory httpClients, AzureSpeechOptions options, AiLatencyTracker latency, ILogger<AzureTranscriptionService> logger) : ITranscriptionService
{
    public const string Operation = "transcribe";

    /// <summary>Pinned because the response shape below (<c>combinedPhrases</c>) is this version's. A newer api-version is a deliberate upgrade with a re-read of the contract.</summary>
    public const string ApiVersion = "2024-11-15";

    public string Name => "azure-fast";

    public bool IsEnabled => options.Enabled;

    public async Task<string> TranscribeAsync(byte[] wav, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(wav);
        if (wav.Length == 0)
        {
            return string.Empty;
        }

        if (!options.Enabled)
        {
            throw new InvalidOperationException("Azure Speech is not configured (key or region missing).");
        }

        // Multipart: the clip under "audio", the locale hint under "definition". The definition is not optional —
        // without a locale the service guesses, and a mis-guessed locale transcribes an English argument phonetically.
        using var content = new MultipartFormDataContent();
        using var audio = new ByteArrayContent(wav);
        audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(audio, "audio", "turn.wav");

        var definition = new JsonObject
        {
            ["locales"] = new JsonArray("en-US"),
            ["profanityFilterMode"] = "None",
        };
        using var definitionContent = new StringContent(definition.ToJsonString());
        definitionContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        content.Add(definitionContent, "definition");

        var url = new Uri($"https://{options.Region}.api.cognitive.microsoft.com/speechtotext/transcriptions:transcribe?api-version={ApiVersion}");
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        request.Headers.Add("Ocp-Apim-Subscription-Key", options.Key);

        var client = httpClients.CreateClient(AzureSpeechService.ClientName);
        var started = Stopwatch.GetTimestamp();
        using var response = await client.SendAsync(request, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);
        var elapsed = Stopwatch.GetElapsedTime(started);
        latency.Record(Operation, elapsed.TotalMilliseconds);
        LogCompleted(logger, Name, wav.Length, elapsed.TotalMilliseconds, (int)response.StatusCode);

        if (!response.IsSuccessStatusCode)
        {
            var detail = raw.Length > 300 ? raw[..300] : raw;
            LogFailed(logger, (int)response.StatusCode, detail);
            throw new HttpRequestException($"Azure fast transcription returned {(int)response.StatusCode}: {detail}", null, response.StatusCode);
        }

        return Transcripts.Clean(ExtractText(raw));
    }

    /// <summary><c>combinedPhrases</c> is the whole clip already stitched together; the per-phrase array only carries timings and speaker ids, which a single-speaker turn has no use for.</summary>
    public static string ExtractText(string rawJson)
    {
        try
        {
            if (JsonNode.Parse(rawJson) is not JsonObject root || root["combinedPhrases"] is not JsonArray combined)
            {
                return string.Empty;
            }

            return string.Join(' ', combined.OfType<JsonObject>().Select(p => p["text"]?.GetValue<string>()).Where(t => !string.IsNullOrWhiteSpace(t))).Trim();
        }
        catch (System.Text.Json.JsonException)
        {
            // A shape we do not recognise is an empty transcript, not an exception: the caller turns that into
            // "we didn't catch that", which is true and actionable, where a 500 would look like the microphone broke.
            return string.Empty;
        }
    }

    [LoggerMessage(EventId = 6100, Level = LogLevel.Information, Message = "Transcribe completed via {Provider}: {Bytes} bytes in {ElapsedMs:F0}ms (HTTP {Status})")]
    private static partial void LogCompleted(ILogger logger, string provider, int bytes, double elapsedMs, int status);

    [LoggerMessage(EventId = 6101, Level = LogLevel.Error, Message = "Azure fast transcription rejected the request (HTTP {Status}): {Detail}")]
    private static partial void LogFailed(ILogger logger, int status, string detail);
}
