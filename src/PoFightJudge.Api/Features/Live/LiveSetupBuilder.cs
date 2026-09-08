using System.Text.Json.Nodes;
using PoFightJudge.Api.Features.Ai;

namespace PoFightJudge.Api.Features.Live;

/// <summary>
/// One session's setup. <paramref name="Voice"/> overrides the configured default so each host persona sounds like
/// itself, and <paramref name="FunctionDeclarations"/> carries the tools the host may call — they belong to the
/// debate, not to the transport, so they arrive from the caller rather than being wired in here.
/// </summary>
public sealed record LiveSessionConfig(
    string SystemInstruction,
    string? ResumptionHandle = null,
    string? Voice = null,
    JsonArray? FunctionDeclarations = null);

/// <summary>
/// Builds the Live API <c>setup</c> frame. Checked against the API reference on 2026-09-06: the response modality
/// and the voice live inside <c>generationConfig</c>, everything else sits directly on <c>setup</c>. A published
/// tutorial shows the modality flat; the reference does not, and proto JSON rejects fields in the wrong place.
/// </summary>
public static class LiveSetupBuilder
{
    public static string Build(GeminiModelOptions models, LiveSessionConfig config)
    {
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(config);

        var tools = new JsonArray(new JsonObject { ["googleSearch"] = new JsonObject() });
        if (config.FunctionDeclarations is { Count: > 0 } declarations)
        {
            tools.Add(new JsonObject { ["functionDeclarations"] = declarations.DeepClone() });
        }

        var setup = new JsonObject
        {
            ["model"] = $"models/{models.Live}",
            ["generationConfig"] = new JsonObject
            {
                ["responseModalities"] = new JsonArray("AUDIO"),
                ["speechConfig"] = new JsonObject
                {
                    ["voiceConfig"] = new JsonObject
                    {
                        ["prebuiltVoiceConfig"] = new JsonObject
                        {
                            ["voiceName"] = string.IsNullOrWhiteSpace(config.Voice) ? models.Voice : config.Voice,
                        },
                    },
                },
            },
            ["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray(new JsonObject { ["text"] = config.SystemInstruction }),
            },
            ["tools"] = tools,

            // Both sides are transcribed: the players' words are what the analysis reads, and the host's words are
            // what the caption feed shows.
            ["inputAudioTranscription"] = new JsonObject(),
            ["outputAudioTranscription"] = new JsonObject(),

            // A debate runs longer than the context window holds, so the window slides rather than the session dying.
            ["contextWindowCompression"] = new JsonObject { ["slidingWindow"] = new JsonObject() },

            // Asked for from the first connection: a handle only exists if it was requested before it was needed.
            ["sessionResumption"] = config.ResumptionHandle is null
                ? new JsonObject()
                : new JsonObject { ["handle"] = config.ResumptionHandle },
        };

        return new JsonObject { ["setup"] = setup }.ToJsonString();
    }
}
