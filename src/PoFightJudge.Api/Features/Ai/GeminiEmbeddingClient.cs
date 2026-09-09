using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PoFightJudge.Api.Features.Diagnostics;

namespace PoFightJudge.Api.Features.Ai;

/// <summary>Turns a piece of text into the vector a history search ranks on.</summary>
public interface IGeminiEmbedding
{
    /// <summary>
    /// The vector, or empty when there is none to be had. Empty is not an error anywhere this is used: a fight
    /// that could not be embedded is one the meaning search will not find, and that is a worse search rather than
    /// a lost report or a failed request.
    /// </summary>
    /// <param name="forQuery">
    /// True for something somebody typed, false for something being stored. The two halves are embedded
    /// differently on purpose — a query and a document that mean the same thing sit closer when each is told which
    /// it is.
    /// </param>
    Task<IReadOnlyList<float>> EmbedAsync(string text, bool forQuery, CancellationToken ct = default);
}

/// <summary>
/// The real one: <c>embedContent</c> over the fast client.
/// </summary>
/// <remarks>
/// One call per fight, once, when the analysis is written — and one per search. It is the cheapest thing this app
/// asks a model for by some distance, which is what makes searching a history by meaning affordable at all.
/// </remarks>
public sealed partial class GeminiEmbeddingClient(
    IHttpClientFactory httpClients,
    GeminiModelOptions models,
    AiLatencyTracker latency,
    ILogger<GeminiEmbeddingClient> logger) : IGeminiEmbedding
{
    public const string Operation = "embed";

    public async Task<IReadOnlyList<float>> EmbedAsync(string text, bool forQuery, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        try
        {
            var client = httpClients.CreateClient(GeminiHttpClients.Fast);
            using var content = new StringContent(BuildRequest(models.Embedding, text, forQuery), Encoding.UTF8, "application/json");

            var started = Stopwatch.GetTimestamp();
            using var response = await client.PostAsync(
                new Uri($"v1beta/models/{Uri.EscapeDataString(models.Embedding)}:embedContent", UriKind.Relative),
                content,
                ct);
            var raw = await response.Content.ReadAsStringAsync(ct);
            latency.Record(Operation, Stopwatch.GetElapsedTime(started).TotalMilliseconds);

            if (!response.IsSuccessStatusCode)
            {
                LogFailed(logger, (int)response.StatusCode, GeminiHttp.Truncate(raw, 200));
                return [];
            }

            return ParseVector(raw);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            LogUnavailable(logger, ex);
            return [];
        }
    }

    public static string BuildRequest(string model, string text, bool forQuery) => new JsonObject
    {
        ["model"] = $"models/{model}",
        ["content"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = text }) },
        ["taskType"] = forQuery ? "RETRIEVAL_QUERY" : "RETRIEVAL_DOCUMENT",
    }.ToJsonString();

    /// <summary>The vector, or empty for anything that is not one.</summary>
    public static IReadOnlyList<float> ParseVector(string raw)
    {
        try
        {
            if (JsonNode.Parse(raw) is not JsonObject body || body["embedding"]?["values"] is not JsonArray values)
            {
                return [];
            }

            var vector = new float[values.Count];
            for (var i = 0; i < values.Count; i++)
            {
                vector[i] = values[i] is JsonValue v && v.TryGetValue<float>(out var f) ? f : 0f;
            }

            return vector;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return [];
        }
    }

    [LoggerMessage(EventId = 1201, Level = LogLevel.Warning, Message = "Embedding was refused (HTTP {Status}: {Detail}); this fight will not be findable by meaning.")]
    private static partial void LogFailed(ILogger logger, int status, string detail);

    [LoggerMessage(EventId = 1202, Level = LogLevel.Warning, Message = "The embedding endpoint could not be reached; this fight will not be findable by meaning.")]
    private static partial void LogUnavailable(ILogger logger, Exception ex);
}

/// <summary>
/// The stand-in. Deterministic and offline: the same text always hashes to the same direction, so a test can assert
/// that the closest fight comes back first without a network or a key.
/// </summary>
public sealed class FakeEmbedding : IGeminiEmbedding
{
    public const int Dimensions = 32;

    public Task<IReadOnlyList<float>> EmbedAsync(string text, bool forQuery, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult<IReadOnlyList<float>>([]);
        }

        // A bag of words over a fixed number of buckets. Crude, and exactly enough to be a similarity: two texts
        // that share words point the same way, and two that share none do not.
        var vector = new float[Dimensions];
        foreach (var word in text.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            vector[(uint)string.GetHashCode(word, StringComparison.Ordinal) % Dimensions] += 1;
        }

        return Task.FromResult<IReadOnlyList<float>>(vector);
    }
}
