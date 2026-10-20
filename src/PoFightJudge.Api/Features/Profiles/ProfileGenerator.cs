using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using PoFightJudge.Api.Features.Ai;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Api.Features.Profiles;

/// <summary>
/// The shape a persona is written in when it is read from somebody's play (<see cref="Fighters.FighterPersonaWriter"/>):
/// the schema the model answers against, the tolerant parse of its answer, and the role voice. The schema carries
/// <c>minLength</c> on every free-text field so a thin answer is refused by the API rather than by us. Nobody invents a
/// persona by hand any more, so there is no generator of its own: profiles are the pre-made cast or come from play.
/// </summary>
public static class ProfileGenerator
{
    public const int FieldMinLength = 40;

    public const int MaxOutputTokens = 2048;


    private static readonly string[] HusbandVoices = ["Charon", "Puck", "Fenrir"];
    private static readonly string[] WifeVoices = ["Kore", "Zephyr"];

    public static JsonObject Schema
    {
        get
        {
            static JsonObject Str() => new() { ["type"] = "string" };
            static JsonObject RichStr() => new() { ["type"] = "string", ["minLength"] = FieldMinLength };
            static JsonObject Int() => new() { ["type"] = "integer" };

            var properties = new JsonObject
            {
                ["name"] = Str(),
                ["age"] = Int(),
                ["occupation"] = Str(),
                ["likes"] = RichStr(),
                ["dislikes"] = RichStr(),
                ["commonArguments"] = RichStr(),
                ["philosophy"] = RichStr(),
                ["logicVsEmotion"] = Int(),
                ["jealousy"] = Int(),
                ["holdsGrudges"] = Int(),
                ["patience"] = Int(),
            };

            // Everything defined is required, and nothing else can be. A name listed here with no property behind it
            // is an invalid schema — eight of them were left over from the wider editor — and the fake, which answers
            // from the properties, cannot tell.
            return new JsonObject
            {
                ["type"] = "object",
                ["required"] = new JsonArray([.. properties.Select(p => (JsonNode)p.Key)]),
                ["properties"] = properties,
            };
        }
    }

    /// <summary>Tolerant of fences, missing fields and free-form enum spellings; sliders clamp to 0–100, age into the validator's range.</summary>
    public static CreateProfileRequest Parse(string raw, ProfileRole role)
    {
        Generated? g = null;
        try
        {
            g = JsonSerializer.Deserialize<Generated>(GeminiHttp.IsolateJsonObject(raw), JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            // Defaults below; the caller's completeness check triggers the retry.
        }

        return new CreateProfileRequest
        {
            Role = role,
            Name = string.IsNullOrWhiteSpace(g?.Name) ? "Unnamed Spouse" : g.Name.Trim(),
            Age = Math.Clamp(g?.Age ?? 35, 18, 120),
            Occupation = string.IsNullOrWhiteSpace(g?.Occupation) ? "Unknown" : g.Occupation.Trim(),
            Likes = g?.Likes?.Trim() ?? string.Empty,
            Dislikes = g?.Dislikes?.Trim() ?? string.Empty,
            CommonArguments = g?.CommonArguments?.Trim() ?? string.Empty,
            Philosophy = g?.Philosophy?.Trim() ?? string.Empty,
            LogicVsEmotion = Clamp(g?.LogicVsEmotion),
            Jealousy = Clamp(g?.Jealousy),
            HoldsGrudges = Clamp(g?.HoldsGrudges),
            Patience = Clamp(g?.Patience),
        };
    }

    public static bool IsComplete(CreateProfileRequest p) =>
        !string.IsNullOrWhiteSpace(p.Name) && !string.Equals(p.Name, "Unnamed Spouse", StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(p.Likes) && !string.IsNullOrWhiteSpace(p.Dislikes)
        && !string.IsNullOrWhiteSpace(p.CommonArguments) && !string.IsNullOrWhiteSpace(p.Philosophy);

    /// <summary>A role voice with a little prosody variety, derived from the name so regenerating the same character sounds the same.</summary>
    public static TtsSettingsDto VoiceFor(ProfileRole role, string name)
    {
        var voices = role == ProfileRole.Wife ? WifeVoices : HusbandVoices;
        var hash = name.Aggregate(2166136261u, (h, c) => (h ^ c) * 16777619u);
        return new TtsSettingsDto
        {
            VoiceName = voices[(int)(hash % (uint)voices.Length)],
            Pitch = Math.Round(0.7 + (hash % 81) / 100.0, 2),
            Speed = Math.Round(0.85 + ((hash / 7) % 51) / 100.0, 2),
        };
    }

    private static int Clamp(int? v) => Math.Clamp(v ?? 50, 0, 100);

    /// <summary>Deserialization target (Web/camelCase JSON); every member optional so a partial answer still parses.</summary>
    private sealed record Generated(
        string? Name,
        int? Age,
        string? Occupation,
        string? Likes,
        string? Dislikes,
        string? CommonArguments,
        string? Philosophy,
        int? LogicVsEmotion,
        int? Jealousy,
        int? HoldsGrudges,
        int? Patience);
}
