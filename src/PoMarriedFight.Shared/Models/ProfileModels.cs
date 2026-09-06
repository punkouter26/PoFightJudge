using System.Text.Json.Serialization;
using PoMarriedFight.Shared.Identifiers;

namespace PoMarriedFight.Shared.Models;

/// <summary>Which side of the marriage a WATCH persona argues. Serialized by name so the wire reads "Wife", not 1.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ProfileRole>))]
public enum ProfileRole
{
    Husband,
    Wife,
}

[JsonConverter(typeof(JsonStringEnumConverter<LoveLanguage>))]
public enum LoveLanguage
{
    WordsOfAffirmation,
    ActsOfService,
    ReceivingGifts,
    QualityTime,
    PhysicalTouch,
}

[JsonConverter(typeof(JsonStringEnumConverter<AttachmentStyle>))]
public enum AttachmentStyle
{
    Secure,
    Anxious,
    Avoidant,
}

[JsonConverter(typeof(JsonStringEnumConverter<StressResponse>))]
public enum StressResponse
{
    Fight,
    Flight,
    Freeze,
    Fawn,
}

/// <summary>Voice configuration for a persona. Mutable because it is bound straight into the editor form.</summary>
public sealed class TtsSettingsDto
{
    public double Pitch { get; set; } = 1.0;

    public double Speed { get; set; } = 1.0;

    public string VoiceName { get; set; } = "Charon";

    /// <summary>Optional Fish Audio voice reference id. Set: the persona speaks with a cloned Fish voice; empty: Gemini's prebuilt voice.</summary>
    public string? FishReferenceId { get; set; }
}

/// <summary>
/// Everything an author can set on a WATCH persona. One shape serves three jobs: the <c>POST /api/profiles</c> body,
/// the <c>PUT /api/profiles/{id}</c> body, and the editor's form model — so it is a mutable class with the same field
/// names and 0–100 slider ranges as the aggregate. Nothing derived (record, stats, face bytes) travels in it.
/// </summary>
public sealed class CreateProfileRequest
{
    public string Initials { get; set; } = string.Empty;

    public ProfileRole Role { get; set; }

    public string? Name { get; set; }

    public int? Age { get; set; }

    public string? Occupation { get; set; }

    public string Likes { get; set; } = string.Empty;

    public string Dislikes { get; set; } = string.Empty;

    public bool IsIntrovert { get; set; }

    public bool IsStubborn { get; set; }

    public bool IsSpontaneous { get; set; }

    public bool IsSarcastic { get; set; }

    public bool IsWorkaholic { get; set; }

    public bool IsPackRat { get; set; }

    public LoveLanguage? LoveLanguage { get; set; }

    public AttachmentStyle? AttachmentStyle { get; set; }

    public StressResponse? StressResponse { get; set; }

    public int LogicVsEmotion { get; set; } = 50;

    public int Punctuality { get; set; } = 50;

    public int InLawAffinity { get; set; } = 50;

    public int ScreenTime { get; set; } = 50;

    public int Jealousy { get; set; } = 50;

    public int IsMessy { get; set; } = 50;

    public int SpendsMoneyFreely { get; set; } = 50;

    public int HoldsGrudges { get; set; } = 50;

    public int Patience { get; set; } = 50;

    public string CommonArguments { get; set; } = string.Empty;

    public string Philosophy { get; set; } = string.Empty;

    public TtsSettingsDto TtsSettings { get; set; } = new();
}

/// <summary>A stored persona as the client sees it: identity, whether a face image exists, and the editable fields.</summary>
public sealed record ProfileDto
{
    public required ProfileId Id { get; init; }

    /// <summary>True when <c>GET /api/profiles/{id}/face</c> has an image to serve.</summary>
    public bool HasFace { get; init; }

    public required CreateProfileRequest Persona { get; init; }
}

/// <summary>Result of <c>POST /api/seed/profiles</c>: how many personas were written and how many got a bundled face.</summary>
public sealed record SeedResultDto(int Seeded, IReadOnlyList<string> Initials, int Faces);

/// <summary>
/// Synthesized speech on the wire. The format is carried with the bytes because the server's provider chain can fall
/// back mid-line: the browser decodes on this value, never on what was asked for.
/// </summary>
public sealed record TtsAudioDto(string Base64, string Format)
{
    public static TtsAudioDto None { get; } = new(string.Empty, "pcm");

    public bool IsEmpty => string.IsNullOrEmpty(Base64);
}

/// <summary>One in-character line spoken in the persona's own voice, for auditioning a persona before it is saved.</summary>
public sealed record PreviewLineResponse(string Line, string Mood, TtsAudioDto Audio);

