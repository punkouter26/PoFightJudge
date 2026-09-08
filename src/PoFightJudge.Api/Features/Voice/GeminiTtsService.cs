using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PoFightJudge.Api.Features.Ai;
using PoFightJudge.Api.Features.Diagnostics;
using PoFightJudge.Api.Features.Profiles;

namespace PoFightJudge.Api.Features.Voice;

/// <summary>
/// Gemini TTS over the Interactions API (the surface the speech docs document for <c>gemini-3.1-flash-tts-preview</c>,
/// verified 2026-09-06): <c>POST v1beta/interactions</c> with <c>response_format.type = audio</c> and one
/// <c>speech_config</c> voice; the answer carries base64 PCM (24 kHz, 16-bit, mono) in <c>output_audio.data</c>.
/// There is no numeric pitch/rate knob, so a persona's sliders are rendered as a spoken-style instruction prefix.
/// </summary>
public sealed partial class GeminiTtsService(IHttpClientFactory httpClients, GeminiModelOptions models, AiLatencyTracker latency, TimeProvider clock, ILogger<GeminiTtsService> logger) : ITtsProvider
{
    public const string Operation = "tts";

    public static Uri Endpoint { get; } = new("v1beta/interactions", UriKind.Relative);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Name => "gemini";

    public bool IsFake => false;

    public async Task<TtsAudio> SynthesizeAsync(string text, TtsSettings settings, CancellationToken ct = default)
    {
        var client = httpClients.CreateClient(GeminiHttpClients.Tts);
        using var content = new StringContent(BuildRequest(models.Tts, text, settings).ToJsonString(JsonOptions), Encoding.UTF8, "application/json");

        var started = Stopwatch.GetTimestamp();
        using var response = await client.PostAsync(Endpoint, content, ct);
        await GeminiHttp.EnsureSuccessAsync(response, "tts interactions.create", ct);
        var node = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct)) as JsonObject;

        // An interaction can come back still running; poll by id, quickly at first, inside the client budget.
        var delay = TimeSpan.FromMilliseconds(200);
        while (node?["status"]?.GetValue<string>() is { } status && status is not ("completed" or "failed") && node["id"]?.GetValue<string>() is { } id)
        {
            await Task.Delay(delay, clock, ct);
            delay = delay < TimeSpan.FromSeconds(3) ? delay * 2 : delay;
            using var poll = await client.GetAsync(new Uri($"v1beta/{Uri.EscapeDataString(id)}", UriKind.Relative), ct);
            await GeminiHttp.EnsureSuccessAsync(poll, "tts interactions.get", ct);
            node = JsonNode.Parse(await poll.Content.ReadAsStringAsync(ct)) as JsonObject;
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        latency.Record(Operation, elapsed.TotalMilliseconds);
        if (string.Equals(node?["status"]?.GetValue<string>(), "failed", StringComparison.Ordinal))
        {
            throw new HttpRequestException("Gemini tts failed: " + GeminiHttp.Truncate(node?["error"]?.ToJsonString() ?? "unknown"));
        }

        var audio = ExtractAudio(node);
        LogSynthesized(logger, models.Tts, settings.VoiceName, text.Length, elapsed.TotalMilliseconds, audio.IsEmpty);
        return audio;
    }

    public static JsonObject BuildRequest(string model, string text, TtsSettings settings) => new()
    {
        ["model"] = model,
        ["input"] = ApplyVoiceStyle(text, settings),
        ["response_format"] = new JsonObject { ["type"] = "audio" },
        ["generation_config"] = new JsonObject
        {
            ["speech_config"] = new JsonArray(new JsonObject { ["voice"] = settings.VoiceName }),
        },
    };

    /// <summary>
    /// Turns a persona's numeric pitch (~0.7–1.5) and speed (~0.85–1.35) into the natural-language direction Gemini TTS
    /// honours. Neutral settings add no prefix so lines stay natural.
    /// </summary>
    public static string ApplyVoiceStyle(string text, TtsSettings settings)
    {
        var styles = new List<string>(2);
        if (settings.Pitch <= 0.85)
        {
            styles.Add("in a low, deep voice");
        }
        else if (settings.Pitch >= 1.2)
        {
            styles.Add("in a high-pitched voice");
        }

        if (settings.Speed <= 0.92)
        {
            styles.Add("slowly and deliberately");
        }
        else if (settings.Speed >= 1.15)
        {
            styles.Add("quickly");
        }

        return styles.Count == 0 ? text : $"Say {string.Join(", ", styles)}: {text}";
    }

    /// <summary>
    /// <c>output_audio.data</c> is what the SDKs expose; the raw resource also lists audio content items under
    /// <c>outputs</c> or <c>steps[].content</c>, so both are read. Empty when the answer carried no audio.
    /// </summary>
    public static TtsAudio ExtractAudio(JsonObject? node)
    {
        if (node?["output_audio"]?["data"]?.GetValue<string>() is { Length: > 0 } direct)
        {
            return TtsAudio.Pcm(direct);
        }

        var items = ((node?["outputs"] as JsonArray)?.OfType<JsonObject>() ?? [])
            .Concat(((node?["steps"] as JsonArray)?.OfType<JsonObject>() ?? []).SelectMany(s => (s["content"] as JsonArray)?.OfType<JsonObject>() ?? []));
        foreach (var item in items)
        {
            if (string.Equals(item["type"]?.GetValue<string>(), "audio", StringComparison.Ordinal) && item["data"]?.GetValue<string>() is { Length: > 0 } data)
            {
                return TtsAudio.Pcm(data);
            }
        }

        return TtsAudio.None;
    }

    [LoggerMessage(EventId = 3101, Level = LogLevel.Information, Message = "Gemini TTS {Model} voice {Voice} synthesized {Chars} chars in {ElapsedMs:F0}ms (empty: {Empty})")]
    private static partial void LogSynthesized(ILogger logger, string model, string voice, int chars, double elapsedMs, bool empty);
}
