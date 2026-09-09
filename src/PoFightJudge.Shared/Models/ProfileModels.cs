using System.Text.Json.Serialization;
using PoFightJudge.Shared.Identifiers;

namespace PoFightJudge.Shared.Models;

/// <summary>Which side of the marriage a WATCH persona argues. Serialized by name so the wire reads "Wife", not 1.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ProfileRole>))]
public enum ProfileRole
{
    Husband,
    Wife,
}

/// <summary>Voice configuration for a persona. Mutable because it is bound straight into the editor form.</summary>
public sealed class TtsSettingsDto
{
    public double Pitch { get; set; } = 1.0;

    public double Speed { get; set; } = 1.0;

    public string VoiceName { get; set; } = "Charon";
}

/// <summary>
/// Everything an author can set on a WATCH persona: who they are, what they argue about, and four dials for how
/// they argue. One shape serves three jobs: the <c>POST /api/profiles</c> body,
/// the <c>PUT /api/profiles/{id}</c> body, and the editor's form model — so it is a mutable class with the same field
/// names and 0–100 slider ranges as the aggregate. Nothing derived (record, stats, face bytes) travels in it.
/// </summary>
/// <remarks>
/// Half the fields this used to carry are gone: six personality toggles, three psychology enums and five sliders
/// about punctuality, in-laws, screen time, mess and money. They were authored once and then defaulted forever, so
/// a typical persona spent nine prompt lines saying "50 (neutral)" — noise the model had to read past to reach the
/// four sentences that actually said who somebody was. What is left is what the preview prompt always used, plus
/// the four dials that change how an argument is conducted rather than what it is about.
/// </remarks>
public sealed class CreateProfileRequest
{
    public string Initials { get; set; } = string.Empty;

    public ProfileRole Role { get; set; }

    public string? Name { get; set; }

    public int? Age { get; set; }

    public string? Occupation { get; set; }

    public string Likes { get; set; } = string.Empty;

    public string Dislikes { get; set; } = string.Empty;

    public int LogicVsEmotion { get; set; } = 50;

    public int Jealousy { get; set; } = 50;

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

    /// <summary>
    /// True for a persona read from somebody's 2P fights, which is rewritten after each one. The authored cast is
    /// never touched, and the editor cannot set this: it is the pipeline's mark, not a field.
    /// </summary>
    public bool FromFights { get; init; }
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

    /// <summary>
    /// Joins the clauses of one streamed line back into a single clip, so what a replay plays back is the whole
    /// line rather than its first phrase. The bytes are concatenated, never the base64: two padded strings glued
    /// together do not decode.
    /// </summary>
    /// <remarks>
    /// Only parts that agree on a format can be joined — 16-bit PCM appends sample by sample and MP3 frame by
    /// frame, but one of each is not a clip in either format. A chain that fell back mid-line therefore archives
    /// nothing, which the replay already reads as a line that has no audio.
    /// </remarks>
    public static TtsAudioDto? Join(IReadOnlyList<TtsAudioDto> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        var usable = parts.Where(p => !p.IsEmpty).ToList();
        if (usable.Count == 0)
        {
            return null;
        }

        var format = usable[0].Format;
        if (usable.Any(p => !string.Equals(p.Format, format, StringComparison.Ordinal)))
        {
            return null;
        }

        if (usable.Count == 1)
        {
            return usable[0];
        }

        var decoded = usable.Select(p => Convert.FromBase64String(p.Base64)).ToList();
        var joined = new byte[decoded.Sum(b => b.Length)];
        var at = 0;
        foreach (var bytes in decoded)
        {
            bytes.CopyTo(joined, at);
            at += bytes.Length;
        }

        return new TtsAudioDto(Convert.ToBase64String(joined), format);
    }
}

/// <summary>One in-character line spoken in the persona's own voice, for auditioning a persona before it is saved.</summary>
public sealed record PreviewLineResponse(string Line, string Mood, TtsAudioDto Audio);

