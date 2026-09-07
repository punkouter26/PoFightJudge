using System.Text;
using System.Text.Json.Nodes;
using PoMarriedFight.Api.Features.Ai;
using PoMarriedFight.Api.Features.Live;
using PoMarriedFight.Shared.Models;

namespace PoMarriedFight.Api.Features.Analysis;

/// <summary>Diarized transcription of a whole recording, used when the live captions are missing or unusable.</summary>
public interface IGeminiTranscribeClient
{
    Task<TranscriptDto> TranscribeAsync(string fileUri, string mimeType, CancellationToken ct);
}

public sealed class GeminiTranscribeClient(IHttpClientFactory factory, GeminiModelOptions models, TimeProvider clock) : IGeminiTranscribeClient
{
    public async Task<TranscriptDto> TranscribeAsync(string fileUri, string mimeType, CancellationToken ct)
    {
        using var http = factory.CreateClient(GeminiHttpClients.Analysis);
        using var content = new StringContent(BuildRequest(models.Transcribe, fileUri, mimeType), Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(new Uri("v1beta/interactions", UriKind.Relative), content, ct);
        await GeminiHttp.EnsureSuccessAsync(response, "interactions.create", ct);
        var node = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));

        // The interaction may still be running; poll it by id until it finishes, quickly at first.
        var backoff = new PollBackoff(TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(3), TimeSpan.FromMinutes(4), clock);
        while (node?["status"]?.GetValue<string>() is { } status
            && status is not ("completed" or "failed")
            && node["id"] is { } id
            && await backoff.WaitAsync(ct))
        {
            using var poll = await http.GetAsync(new Uri($"v1beta/{id.GetValue<string>()}", UriKind.Relative), ct);
            await GeminiHttp.EnsureSuccessAsync(poll, "interactions.get", ct);
            node = JsonNode.Parse(await poll.Content.ReadAsStringAsync(ct));
        }

        if (string.Equals(node?["status"]?.GetValue<string>(), "failed", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Transcription failed: " + (node?["error"]?.ToJsonString() ?? "unknown"));
        }

        return ParseResponse(node);
    }

    public static string BuildRequest(string model, string fileUri, string mimeType) => new JsonObject
    {
        ["model"] = model,
        ["input"] = new JsonArray(new JsonObject { ["type"] = "audio", ["uri"] = fileUri, ["mime_type"] = mimeType }),
        ["generation_config"] = new JsonObject
        {
            ["transcription_config"] = new JsonObject
            {
                // Without the granularity the response carries text only, and the words have no offsets to map.
                ["mode"] = new JsonObject
                {
                    ["type"] = "verbatim",
                    ["diarization_mode"] = "speaker",
                    ["timestamp_granularities"] = new JsonArray("word"),
                },
            },
        },
    }.ToJsonString();

    /// <summary>
    /// Reads whichever shape the answer came back in. The words matter more than the text: without offsets there is
    /// nothing to attribute to a speaker.
    /// </summary>
    public static TranscriptDto ParseResponse(JsonNode? root)
    {
        var words = new List<TranscriptWord>();
        var text = new StringBuilder();
        var contents = (root?["steps"] as JsonArray)?.OfType<JsonObject>()
                .Where(s => s["type"]?.GetValue<string>() is null or "model_output")
                .SelectMany(s => (s["content"] as JsonArray)?.OfType<JsonObject>() ?? [])
            ?? (root?["outputs"] as JsonArray)?.OfType<JsonObject>()
            ?? [];

        foreach (var content in contents)
        {
            if (content["text"] is { } spoken)
            {
                if (text.Length > 0)
                {
                    text.Append(' ');
                }

                text.Append(spoken.GetValue<string>());
            }

            foreach (var annotation in (content["annotations"] as JsonArray)?.OfType<JsonObject>() ?? [])
            {
                if (annotation["type"]?.GetValue<string>() is not (null or "word_info"))
                {
                    continue;
                }

                var word = annotation["text"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(word))
                {
                    continue;
                }

                words.Add(new TranscriptWord(
                    word,
                    annotation["speaker"]?.GetValue<string>() ?? "spk_1",
                    LiveMessageParser.ParseDuration(annotation["start_offset"]?.ToString()).TotalSeconds,
                    LiveMessageParser.ParseDuration(annotation["end_offset"]?.ToString()).TotalSeconds));
            }
        }

        return new TranscriptDto(text.ToString(), words);
    }
}
