using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using PoMarriedFight.Api.Features.Ai;
using PoMarriedFight.Shared.Models;

namespace PoMarriedFight.Api.Features.Profiles;

/// <summary>Invents a WATCH persona for a role, distinct from the cast that already exists. Returns an unsaved draft for the editor.</summary>
public interface IProfileGenerator
{
    Task<CreateProfileRequest> GenerateAsync(ProfileRole role, IReadOnlyList<Profile> existing, CancellationToken ct = default);
}

/// <summary>
/// One schema-constrained <c>gemini-3.7-flash</c> call (the fake answers the same schema). The schema carries
/// <c>minLength</c> on every free-text field so a thin result is rejected by the API rather than by us; one retry
/// remains for the rare answer that satisfies the schema and is still filler. The draft gets unique initials and a
/// role voice here; the user reviews it in the editor and decides whether it joins the cast.
/// </summary>
public sealed partial class ProfileGenerator(IGeminiText gemini, GeminiModelOptions models, ILogger<ProfileGenerator> logger) : IProfileGenerator
{
    public const string Operation = "profile";

    public const int FieldMinLength = 40;

    public const int MaxOutputTokens = 2048;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly string[] HusbandVoices = ["Charon", "Puck", "Fenrir"];
    private static readonly string[] WifeVoices = ["Kore", "Zephyr"];

    public static JsonObject Schema
    {
        get
        {
            static JsonObject Str() => new() { ["type"] = "string" };
            static JsonObject RichStr() => new() { ["type"] = "string", ["minLength"] = FieldMinLength };
            static JsonObject Int() => new() { ["type"] = "integer" };
            static JsonObject Bool() => new() { ["type"] = "boolean" };
            static JsonObject Enum<T>()
                where T : struct, Enum =>
                new() { ["type"] = "string", ["enum"] = new JsonArray([.. System.Enum.GetNames<T>().Select(n => (JsonNode)n)]) };

            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["name"] = Str(),
                    ["age"] = Int(),
                    ["occupation"] = Str(),
                    ["likes"] = RichStr(),
                    ["dislikes"] = RichStr(),
                    ["commonArguments"] = RichStr(),
                    ["philosophy"] = RichStr(),
                    ["isIntrovert"] = Bool(),
                    ["isStubborn"] = Bool(),
                    ["isSpontaneous"] = Bool(),
                    ["isSarcastic"] = Bool(),
                    ["isWorkaholic"] = Bool(),
                    ["isPackRat"] = Bool(),
                    ["loveLanguage"] = Enum<LoveLanguage>(),
                    ["attachmentStyle"] = Enum<AttachmentStyle>(),
                    ["stressResponse"] = Enum<StressResponse>(),
                    ["logicVsEmotion"] = Int(),
                    ["punctuality"] = Int(),
                    ["inLawAffinity"] = Int(),
                    ["screenTime"] = Int(),
                    ["jealousy"] = Int(),
                    ["isMessy"] = Int(),
                    ["spendsMoneyFreely"] = Int(),
                    ["holdsGrudges"] = Int(),
                    ["patience"] = Int(),
                },
                ["required"] = new JsonArray(
                    "name", "age", "occupation", "likes", "dislikes", "commonArguments", "philosophy",
                    "loveLanguage", "attachmentStyle", "stressResponse",
                    "logicVsEmotion", "punctuality", "inLawAffinity", "screenTime", "jealousy",
                    "isMessy", "spendsMoneyFreely", "holdsGrudges", "patience"),
            };
        }
    }

    public async Task<CreateProfileRequest> GenerateAsync(ProfileRole role, IReadOnlyList<Profile> existing, CancellationToken ct = default)
    {
        var roster = existing.Select(p => $"{p.Name ?? p.Initials} — {p.Occupation ?? "?"}").ToList();
        CreateProfileRequest draft = new();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var request = new GeminiTextRequest(BuildPrompt(role, roster, attempt), models.Profile, Operation, Schema, Temperature: 1.0, MaxOutputTokens: MaxOutputTokens, ThinkingLevel: "low");
            draft = Parse(await gemini.GenerateAsync(request, ct), role);
            if (IsComplete(draft))
            {
                break;
            }
        }

        draft.Initials = MakeUniqueInitials(draft.Name ?? string.Empty, existing.Select(p => p.Initials));
        draft.TtsSettings = VoiceFor(role, draft.Name ?? draft.Initials);
        LogGenerated(logger, role, draft.Initials, draft.Name ?? "?");
        return draft;
    }

    /// <summary>
    /// The instructions are identical for every persona ever generated; only the roster to avoid changes — so the
    /// constant half is cacheable across a whole seeding run instead of being re-sent per character.
    /// </summary>
    public static GeminiPrompt BuildPrompt(ProfileRole role, IReadOnlyList<string> roster, int attempt)
    {
        var avoid = roster.Count == 0 ? "(none yet)" : string.Join("\n", roster.Select(d => $"  • {d}"));
        var nudge = attempt == 0
            ? string.Empty
            : "\nThe previous attempt was too thin — this time fill EVERY field with vivid, specific detail and make the character even more unlike the roster above.";

        const string system = """
            You are creating fictional characters for a satirical "married couple argument simulator".
            Every character must be memorable and specific — an unusual job, oddly specific pet peeves,
            and a personality that is clearly their own. Vary wildly from typical characters; surprise me.

            Fill the JSON schema. Every field is required. The likes, dislikes, commonArguments and
            philosophy MUST be unique to this character — never generic filler like "coffee, walks"
            or "keep it simple". Age 24-68. Sliders are 0-100.
            """;

        var user = $"""
            Invent ONE vivid, distinctive {role.ToString().ToLowerInvariant()}.

            MUST BE CLEARLY DIFFERENT from these existing characters (different name, occupation,
            vibe, likes/dislikes — do not echo them):
            {avoid}{nudge}
            """;

        return new GeminiPrompt(system, user);
    }

    /// <summary>Tolerant of fences, missing fields and free-form enum spellings; sliders clamp to 0–100, age into the validator's range.</summary>
    public static CreateProfileRequest Parse(string raw, ProfileRole role)
    {
        Generated? g = null;
        try
        {
            g = JsonSerializer.Deserialize<Generated>(IsolateJsonObject(raw), Json);
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
            IsIntrovert = g?.IsIntrovert ?? false,
            IsStubborn = g?.IsStubborn ?? false,
            IsSpontaneous = g?.IsSpontaneous ?? false,
            IsSarcastic = g?.IsSarcastic ?? false,
            IsWorkaholic = g?.IsWorkaholic ?? false,
            IsPackRat = g?.IsPackRat ?? false,
            LoveLanguage = NormalizeEnum(g?.LoveLanguage, LoveLanguage.QualityTime),
            AttachmentStyle = NormalizeEnum(g?.AttachmentStyle, AttachmentStyle.Secure),
            StressResponse = NormalizeEnum(g?.StressResponse, StressResponse.Fight),
            LogicVsEmotion = Clamp(g?.LogicVsEmotion),
            Punctuality = Clamp(g?.Punctuality),
            InLawAffinity = Clamp(g?.InLawAffinity),
            ScreenTime = Clamp(g?.ScreenTime),
            Jealousy = Clamp(g?.Jealousy),
            IsMessy = Clamp(g?.IsMessy),
            SpendsMoneyFreely = Clamp(g?.SpendsMoneyFreely),
            HoldsGrudges = Clamp(g?.HoldsGrudges),
            Patience = Clamp(g?.Patience),
        };
    }

    public static bool IsComplete(CreateProfileRequest p) =>
        !string.IsNullOrWhiteSpace(p.Name) && !string.Equals(p.Name, "Unnamed Spouse", StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(p.Likes) && !string.IsNullOrWhiteSpace(p.Dislikes)
        && !string.IsNullOrWhiteSpace(p.CommonArguments) && !string.IsNullOrWhiteSpace(p.Philosophy);

    /// <summary>First letters of up to three name words, padded from the first word; a taken tag gets its last character cycled.</summary>
    public static string MakeUniqueInitials(string name, IEnumerable<string> existing)
    {
        var taken = new HashSet<string>(existing.Select(Initials.Normalize), StringComparer.Ordinal);
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(w => new string([.. w.Where(char.IsAsciiLetterOrDigit)]))
            .Where(w => w.Length > 0)
            .ToList();
        var seed = string.Concat(words.Take(3).Select(w => w[0]));
        foreach (var extra in words.SelectMany(w => w.Skip(1)))
        {
            if (seed.Length >= 3)
            {
                break;
            }

            seed += extra;
        }

        var candidate = Initials.Normalize(seed.Length == 0 ? "AI" : seed);
        if (!taken.Contains(candidate))
        {
            return candidate;
        }

        var head = candidate.Length >= 2 ? candidate[..2] : candidate;
        foreach (var tail in "23456789ABCDEFGHJKLMNPQRSTUVWXYZ")
        {
            var next = head + tail;
            if (!taken.Contains(next))
            {
                return next;
            }
        }

        return Initials.Normalize(Guid.NewGuid().ToString("N")[..3]);
    }

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

    private static T NormalizeEnum<T>(string? value, T fallback)
        where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var cleaned = value.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        return Enum.TryParse<T>(cleaned, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) ? parsed : fallback;
    }

    private static string IsolateJsonObject(string raw)
    {
        var start = raw.IndexOf('{', StringComparison.Ordinal);
        var end = raw.LastIndexOf('}');
        return start >= 0 && end > start ? raw[start..(end + 1)] : raw;
    }

    [LoggerMessage(EventId = 1101, Level = LogLevel.Information, Message = "Generated a {Role} persona {Initials} ({Name})")]
    private static partial void LogGenerated(ILogger logger, ProfileRole role, string initials, string name);

    /// <summary>Deserialization target (Web/camelCase JSON); every member optional so a partial answer still parses.</summary>
    private sealed record Generated(
        string? Name,
        int? Age,
        string? Occupation,
        string? Likes,
        string? Dislikes,
        string? CommonArguments,
        string? Philosophy,
        bool? IsIntrovert,
        bool? IsStubborn,
        bool? IsSpontaneous,
        bool? IsSarcastic,
        bool? IsWorkaholic,
        bool? IsPackRat,
        string? LoveLanguage,
        string? AttachmentStyle,
        string? StressResponse,
        int? LogicVsEmotion,
        int? Punctuality,
        int? InLawAffinity,
        int? ScreenTime,
        int? Jealousy,
        int? IsMessy,
        int? SpendsMoneyFreely,
        int? HoldsGrudges,
        int? Patience);
}
