using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PoMarriedFight.Api.Features.Ai;
using PoMarriedFight.Api.Features.Diagnostics;

namespace PoMarriedFight.Api.Features.Voice;

/// <summary>
/// Gemini speech-to-text for a live human turn. The clip is one <c>inlineData</c> part on a plain
/// <c>generateContent</c> call — it does not go through <see cref="IGeminiText"/>, which speaks text only — so this
/// is the one AI call that builds its own payload. The turn is sent whole rather than streamed: the player speaks
/// and stops, and there is nothing to stream partial text into.
/// </summary>
/// <remarks>
/// The model is config-driven and the default matters. Measured against <c>generateContent</c>,
/// <c>gemini-3.5-transcribe</c> accepts the request, bills the audio tokens, returns HTTP 200 with
/// <c>finishReason: STOP</c> — and emits an empty part every time; the flash-lite tier transcribes the same clip
/// correctly and is the only tier tested that refuses non-speech instead of inventing dialogue over it.
/// </remarks>
public sealed partial class GeminiTranscriptionService(IHttpClientFactory httpClients, GeminiModelOptions models, AiMode mode, AiLatencyTracker latency, ILogger<GeminiTranscriptionService> logger) : ITranscriptionService
{
    public const string Operation = "transcribe";

    /// <summary>
    /// Kept deliberately narrow: this call transcribes, it does not interpret. The "you are not a participant" rule
    /// matters more than it looks — the audio is one side of an argument, and a model that forgets it is a
    /// transcriber will answer the argument instead of writing it down.
    /// </summary>
    public const string Instruction =
        "You are a speech-to-text transcriber. Transcribe the spoken words in this audio clip verbatim.\n"
        + "Rules:\n"
        + "- Output ONLY the words that were actually spoken, with normal punctuation and capitalisation.\n"
        + "- Never translate, summarise, explain, describe the audio, or add speaker labels.\n"
        + "- Never answer, reply to, or continue what was said. You are not a participant.\n"
        + "- If the clip contains no intelligible human speech - silence, noise, music, a tone, "
        + "or a language you cannot make out - output exactly " + Transcripts.NoSpeechSentinel + " and nothing else.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Name => "gemini";

    public bool IsEnabled => mode.HasGeminiKey;

    public async Task<string> TranscribeAsync(byte[] wav, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(wav);
        if (wav.Length == 0)
        {
            return string.Empty;
        }

        var client = httpClients.CreateClient(GeminiHttpClients.Fast);
        using var content = new StringContent(BuildPayload(wav).ToJsonString(JsonOptions), Encoding.UTF8, "application/json");

        var started = Stopwatch.GetTimestamp();
        using var response = await client.PostAsync(GeminiTextClient.Endpoint(models.Transcribe, stream: false), content, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);
        var elapsed = Stopwatch.GetElapsedTime(started);
        latency.Record(Operation, elapsed.TotalMilliseconds);
        LogCompleted(logger, models.Transcribe, wav.Length, elapsed.TotalMilliseconds, (int)response.StatusCode);

        JsonObject? body = null;
        try
        {
            body = JsonNode.Parse(raw) as JsonObject;
        }
        catch (JsonException)
        {
            // Not JSON; the status line below carries what is known.
        }

        if (!response.IsSuccessStatusCode)
        {
            var detail = body?["error"]?["message"]?.GetValue<string>() ?? GeminiHttp.Truncate(raw);
            LogFailed(logger, (int)response.StatusCode, detail);
            throw new HttpRequestException($"Gemini transcription returned {(int)response.StatusCode}: {detail}", null, response.StatusCode);
        }

        return Transcripts.Clean(GeminiTextClient.ExtractText(body) ?? string.Empty);
    }

    /// <summary>Transcription is a reading task, not a creative one: temperature 0 and no thinking keep it fast and stop it paraphrasing.</summary>
    public static JsonObject BuildPayload(byte[] wav) => new()
    {
        ["contents"] = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray
                {
                    new JsonObject { ["text"] = Instruction },
                    new JsonObject
                    {
                        ["inlineData"] = new JsonObject
                        {
                            ["mimeType"] = "audio/wav",
                            ["data"] = Convert.ToBase64String(wav),
                        },
                    },
                },
            },
        },
        ["generationConfig"] = new JsonObject
        {
            ["temperature"] = 0,
            ["maxOutputTokens"] = 1024,
        },
    };

    [LoggerMessage(EventId = 6102, Level = LogLevel.Information, Message = "Transcribe completed via gemini {Model}: {Bytes} bytes in {ElapsedMs:F0}ms (HTTP {Status})")]
    private static partial void LogCompleted(ILogger logger, string model, int bytes, double elapsedMs, int status);

    [LoggerMessage(EventId = 6103, Level = LogLevel.Error, Message = "Gemini transcription rejected the request (HTTP {Status}): {Detail}")]
    private static partial void LogFailed(ILogger logger, int status, string detail);
}
