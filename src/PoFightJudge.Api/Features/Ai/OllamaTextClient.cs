using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PoFightJudge.Api.Features.Diagnostics;
using PoFightJudge.Shared.Configuration;

namespace PoFightJudge.Api.Features.Ai;

/// <summary>
/// Where a local model lives and whether to use one.
/// </summary>
/// <remarks>
/// Never in Production, on the same rule the fakes are held to: registration is guarded and a test asserts the
/// guard. This is a development convenience — an argument that is actually written rather than four canned lines on
/// a loop — and a way to keep working when the quota is gone.
/// </remarks>
public sealed class OllamaOptions
{
    public const string Section = ConfigKeys.Ai.OllamaSection;

    public const string ClientName = "ollama";

    /// <summary>Ollama's own default address. It is a local daemon; there is nothing to configure to reach it.</summary>
    public Uri Endpoint { get; set; } = new("http://localhost:11434/");

    /// <summary>
    /// The model to pull and run. A small instruct model is the right size for this: a WATCH line is 45 words with
    /// a two-field schema, and the whole point is that it answers on a laptop.
    /// </summary>
    public string Model { get; set; } = "qwen3:8b";

    public bool Enabled { get; set; }

    /// <summary>Whether this runtime serves the text seam. Production is never one of the answers.</summary>
    public static bool ShouldUse(OllamaOptions options, bool isProduction)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Enabled && !isProduction;
    }
}

/// <summary>
/// <see cref="IGeminiText"/> over a local Ollama daemon: <c>POST /api/chat</c>, streamed as NDJSON.
/// </summary>
/// <remarks>
/// The seam fits without adaptation, which is the reason this is a small file. The prompt is already split into the
/// halves that become a system and a user message; <see cref="GeminiTextRequest.ResponseSchema"/> is already plain
/// JSON Schema, which is what <c>format</c> takes for structured output; and the runtime already streams the
/// newline-delimited objects <see cref="StreamAsync"/> wants.
///
/// What it cannot do is report a cached prefix, because there is no context cache to bill against — the ledger
/// therefore records zero cached tokens rather than leaving the operation absent, so a session on this runtime does
/// not read as a session whose prompt caching collapsed.
/// </remarks>
public sealed partial class OllamaTextClient(
    IHttpClientFactory httpClients,
    AiLatencyTracker latency,
    ILogger<OllamaTextClient> logger) : IGeminiText
{
    public const string ChatPath = "api/chat";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>False: this is a real model writing real lines, and the "using fake AI" banner must not claim otherwise.</summary>
    public bool IsFake => false;

    public async Task<string> GenerateAsync(GeminiTextRequest request, CancellationToken ct = default)
    {
        var client = httpClients.CreateClient(OllamaOptions.ClientName);
        using var content = new StringContent(BuildPayload(request, stream: false), Encoding.UTF8, "application/json");

        var started = Stopwatch.GetTimestamp();
        using var response = await client.PostAsync(new Uri(ChatPath, UriKind.Relative), content, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);
        var elapsed = Stopwatch.GetElapsedTime(started);

        latency.Record(request.Operation, elapsed.TotalMilliseconds);
        LogCompleted(logger, request.Model, request.Operation, elapsed.TotalMilliseconds, (int)response.StatusCode);

        if (!response.IsSuccessStatusCode)
        {
            var detail = GeminiHttp.Truncate(raw);
            LogFailed(logger, request.Operation, (int)response.StatusCode, detail);
            throw new HttpRequestException($"Ollama {request.Operation} failed: {(int)response.StatusCode} {detail}", null, response.StatusCode);
        }

        var body = TryParse(raw);
        RecordUsage(request.Operation, body);
        return Content(body) ?? throw new HttpRequestException($"Ollama {request.Operation} returned no message content.", null, response.StatusCode);
    }

    public async IAsyncEnumerable<string> StreamAsync(GeminiTextRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var client = httpClients.CreateClient(OllamaOptions.ClientName);
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(ChatPath, UriKind.Relative))
        {
            Content = new StringContent(BuildPayload(request, stream: true), Encoding.UTF8, "application/json"),
        };

        var started = Stopwatch.GetTimestamp();
        using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var error = GeminiHttp.Truncate(await response.Content.ReadAsStringAsync(ct));
            LogFailed(logger, request.Operation, (int)response.StatusCode, error);
            throw new HttpRequestException($"Ollama {request.Operation} stream failed: {(int)response.StatusCode} {error}", null, response.StatusCode);
        }

        var first = true;
        JsonObject? last = null;
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line) || TryParse(line) is not { } chunk)
            {
                continue;
            }

            last = chunk;
            var fragment = Content(chunk);
            if (string.IsNullOrEmpty(fragment))
            {
                // The closing object carries the counts and an empty content; it is the end, not a fragment.
                continue;
            }

            if (first)
            {
                first = false;
                latency.RecordTimeToFirstToken(request.Operation, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }

            yield return fragment;
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        latency.Record(request.Operation, elapsed.TotalMilliseconds);
        LogCompleted(logger, request.Model, request.Operation, elapsed.TotalMilliseconds, (int)response.StatusCode);
        RecordUsage(request.Operation, last);
    }

    /// <summary>
    /// The request body. The prompt's two halves stay two messages — a merged blob would cost the leading-token
    /// cache here exactly as it does on the hosted side — and the schema goes to <c>format</c> unchanged.
    /// </summary>
    public static string BuildPayload(GeminiTextRequest request, bool stream)
    {
        ArgumentNullException.ThrowIfNull(request);

        var messages = new JsonArray();
        if (!string.IsNullOrEmpty(request.Prompt.System))
        {
            messages.Add(new JsonObject { ["role"] = "system", ["content"] = request.Prompt.System });
        }

        messages.Add(new JsonObject { ["role"] = "user", ["content"] = request.Prompt.User });

        var payload = new JsonObject
        {
            ["model"] = request.Model,
            ["messages"] = messages,
            ["stream"] = stream,
            ["options"] = new JsonObject
            {
                ["temperature"] = request.Temperature,
                ["num_predict"] = request.MaxOutputTokens,
            },
        };

        // Only when one was asked for: "format" is how this runtime is told to constrain the answer, and an
        // unconstrained call is prose that a schema would refuse.
        if (request.ResponseSchema is not null)
        {
            payload["format"] = request.ResponseSchema.DeepClone();
        }

        return payload.ToJsonString(JsonOptions);
    }

    /// <summary>The assistant's text, wherever the chunk is in the stream.</summary>
    public static string? Content(JsonObject? chunk) => chunk?["message"]?["content"]?.GetValue<string>();

    private static JsonObject? TryParse(string raw)
    {
        try
        {
            return JsonNode.Parse(raw) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void RecordUsage(string operation, JsonObject? body)
    {
        if (body is null)
        {
            return;
        }

        static int Count(JsonObject body, string key) => body[key] is JsonValue v && v.TryGetValue<int>(out var n) ? n : 0;

        // Zero cached rather than nothing recorded: this runtime has no context cache to bill against, and an
        // absent operation would read as a prompt whose caching collapsed.
        latency.RecordUsage(
            operation,
            promptTokens: Count(body, "prompt_eval_count"),
            outputTokens: Count(body, "eval_count"),
            cachedTokens: 0);
    }

    [LoggerMessage(EventId = 1101, Level = LogLevel.Information, Message = "Ollama {Model} {Operation} completed in {ElapsedMs:F0}ms with {Status}")]
    private static partial void LogCompleted(ILogger logger, string model, string operation, double elapsedMs, int status);

    [LoggerMessage(EventId = 1102, Level = LogLevel.Error, Message = "Ollama {Operation} failed with {Status}: {Detail}")]
    private static partial void LogFailed(ILogger logger, string operation, int status, string detail);
}
