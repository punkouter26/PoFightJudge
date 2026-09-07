using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PoMarriedFight.Api.Features.Live;

/// <summary>What a Gemini Live session tells us, decoded from the raw WebSocket JSON.</summary>
public abstract record LiveServerEvent
{
    public sealed record SetupComplete : LiveServerEvent;

    /// <summary>The host's voice, as raw 24 kHz little-endian PCM.</summary>
    public sealed record AudioOut(ReadOnlyMemory<byte> Pcm24k) : LiveServerEvent;

    public sealed record OutputText(string Text) : LiveServerEvent;

    /// <summary>What the players said, as the model heard it. This is the transcript the analysis prefers.</summary>
    public sealed record InputTranscript(string Text) : LiveServerEvent;

    public sealed record OutputTranscript(string Text) : LiveServerEvent;

    public sealed record Interrupted : LiveServerEvent;

    public sealed record TurnComplete : LiveServerEvent;

    public sealed record GenerationComplete : LiveServerEvent;

    public sealed record ToolCall(string Id, string Name, JsonElement Args) : LiveServerEvent;

    /// <summary>The session is about to be cut; <paramref name="TimeLeft"/> is the warning.</summary>
    public sealed record GoAway(TimeSpan TimeLeft) : LiveServerEvent;

    public sealed record ResumptionHandle(string Handle, bool Resumable) : LiveServerEvent;

    public sealed record Closed(string Reason) : LiveServerEvent;
}

/// <summary>
/// Decoder for Live API server messages. Field names verified against the API reference on 2026-09-06. It is
/// deliberately tolerant: an unknown field yields no event rather than an exception, because a change on Google's
/// side must not end a debate that is halfway through.
/// </summary>
public static class LiveMessageParser
{
    public static IReadOnlyList<LiveServerEvent> Parse(string json)
    {
        var events = new List<LiveServerEvent>();

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return events;
        }

        if (root is not JsonObject message)
        {
            return events;
        }

        if (message["setupComplete"] is not null)
        {
            events.Add(new LiveServerEvent.SetupComplete());
        }

        if (message["serverContent"] is JsonObject content)
        {
            ParseServerContent(content, events);
        }

        if (message["toolCall"]?["functionCalls"] is JsonArray calls)
        {
            foreach (var call in calls.OfType<JsonObject>())
            {
                var name = Text(call["name"]) ?? string.Empty;

                // An id is what a response is addressed to; without one the name is the only handle there is.
                var id = Text(call["id"]) ?? name;
                var args = call["args"] is { } supplied
                    ? JsonSerializer.SerializeToElement(supplied)
                    : JsonSerializer.SerializeToElement(new JsonObject());
                events.Add(new LiveServerEvent.ToolCall(id, name, args));
            }
        }

        if (message["goAway"] is JsonObject goAway)
        {
            events.Add(new LiveServerEvent.GoAway(ParseDuration(Text(goAway["timeLeft"]))));
        }

        if (message["sessionResumptionUpdate"] is JsonObject resumption && Text(resumption["newHandle"]) is { Length: > 0 } handle)
        {
            events.Add(new LiveServerEvent.ResumptionHandle(handle, Flag(resumption["resumable"])));
        }

        return events;
    }

    /// <summary>Protobuf duration strings look like "9.5s".</summary>
    public static TimeSpan ParseDuration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return TimeSpan.Zero;
        }

        return double.TryParse(value.TrimEnd('s'), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.Zero;
    }

    private static void ParseServerContent(JsonObject content, List<LiveServerEvent> events)
    {
        if (Flag(content["interrupted"]))
        {
            events.Add(new LiveServerEvent.Interrupted());
        }

        if (Text(content["inputTranscription"]?["text"]) is { } heard)
        {
            events.Add(new LiveServerEvent.InputTranscript(heard));
        }

        if (Text(content["outputTranscription"]?["text"]) is { } said)
        {
            events.Add(new LiveServerEvent.OutputTranscript(said));
        }

        if (content["modelTurn"]?["parts"] is JsonArray parts)
        {
            foreach (var part in parts.OfType<JsonObject>())
            {
                if (Text(part["inlineData"]?["data"]) is { } audio)
                {
                    if (TryDecode(audio, out var pcm))
                    {
                        events.Add(new LiveServerEvent.AudioOut(pcm));
                    }
                }
                else if (Text(part["text"]) is { } text)
                {
                    events.Add(new LiveServerEvent.OutputText(text));
                }
            }
        }

        if (Flag(content["generationComplete"]))
        {
            events.Add(new LiveServerEvent.GenerationComplete());
        }

        if (Flag(content["turnComplete"]))
        {
            events.Add(new LiveServerEvent.TurnComplete());
        }
    }

    /// <summary>Reads a value as a string, or null when it is absent or is not one. Never throws on a surprise.</summary>
    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static bool Flag(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;

    private static bool TryDecode(string base64, out byte[] pcm)
    {
        var buffer = new byte[((base64.Length * 3) + 3) / 4];
        if (Convert.TryFromBase64String(base64, buffer, out var written))
        {
            pcm = buffer[..written];
            return true;
        }

        pcm = [];
        return false;
    }
}

/// <summary>The frames we send. Shapes verified against the Live API docs on 2026-09-06.</summary>
public static class LiveClientMessages
{
    /// <summary>The Live API takes raw 16-bit PCM at 16 kHz, little-endian.</summary>
    public const string AudioMimeType = "audio/pcm;rate=16000";

    public static string RealtimeAudio(ReadOnlySpan<byte> pcm16k) =>
        new JsonObject
        {
            ["realtimeInput"] = new JsonObject
            {
                ["audio"] = new JsonObject
                {
                    ["data"] = Convert.ToBase64String(pcm16k),
                    ["mimeType"] = AudioMimeType,
                },
            },
        }.ToJsonString();

    /// <summary>A text turn. This is how the orchestrator nudges the host: "Alex has had 45 seconds — cut in".</summary>
    public static string UserText(string text, bool turnComplete = true) =>
        new JsonObject
        {
            ["clientContent"] = new JsonObject
            {
                ["turns"] = new JsonArray(new JsonObject
                {
                    ["role"] = "user",
                    ["parts"] = new JsonArray(new JsonObject { ["text"] = text }),
                }),
                ["turnComplete"] = turnComplete,
            },
        }.ToJsonString();

    public static string ToolResponse(string id, string name, object result) =>
        new JsonObject
        {
            ["toolResponse"] = new JsonObject
            {
                ["functionResponses"] = new JsonArray(new JsonObject
                {
                    ["id"] = id,
                    ["name"] = name,
                    ["response"] = JsonSerializer.SerializeToNode(result),
                }),
            },
        }.ToJsonString();
}
