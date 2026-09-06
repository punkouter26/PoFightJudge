using PoMarriedFight.Shared.Identifiers;
using PoMarriedFight.Shared.Models;

namespace PoMarriedFight.Api.Features.Profiles;

/// <summary>
/// Aggregate root for a WATCH persona: identity plus every personality trait the prompt builders read. Ported from
/// PoMarriedLife with the win/loss counters and stored stats removed — a record is derived from <c>WatchResults</c>
/// on read (SPEC §6), so nothing here can drift from what actually happened.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1724:Type names should not match namespaces", Justification = "Profile is the SPEC term; System.Web.Profile is a legacy .NET Framework namespace this app never references.")]
public sealed class Profile
{
    public ProfileId Id { get; private set; }

    /// <summary>The id is the upper-cased initials; both names are kept because prompts and rows use them differently.</summary>
    public string Initials => Id.Value;

    public string? Name { get; private set; }

    public int? Age { get; private set; }

    public string? Occupation { get; private set; }

    public ProfileRole Role { get; private set; }

    public string Likes { get; private set; } = string.Empty;

    public string Dislikes { get; private set; } = string.Empty;

    public bool IsIntrovert { get; private set; }

    public bool IsStubborn { get; private set; }

    public bool IsSpontaneous { get; private set; }

    public bool IsSarcastic { get; private set; }

    public bool IsWorkaholic { get; private set; }

    public bool IsPackRat { get; private set; }

    public LoveLanguage? LoveLanguage { get; private set; }

    public AttachmentStyle? AttachmentStyle { get; private set; }

    public StressResponse? StressResponse { get; private set; }

    // Sliders: 0–100, 50 = neutral.
    public int LogicVsEmotion { get; private set; } = 50;

    public int Punctuality { get; private set; } = 50;

    public int InLawAffinity { get; private set; } = 50;

    public int ScreenTime { get; private set; } = 50;

    public int Jealousy { get; private set; } = 50;

    public int IsMessy { get; private set; } = 50;

    public int SpendsMoneyFreely { get; private set; } = 50;

    public int HoldsGrudges { get; private set; } = 50;

    public int Patience { get; private set; } = 50;

    public string CommonArguments { get; private set; } = string.Empty;

    public string Philosophy { get; private set; } = string.Empty;

    /// <summary>Blob name of the face image under the faces container, or null when none was uploaded.</summary>
    public string? FacePic { get; private set; }

    public TtsSettings TtsSettings { get; private set; } = TtsSettings.Default(ProfileRole.Husband);

    /// <summary>True when this side is a real person rather than an authored persona. Never stored: a human has a fighter row, not a profile.</summary>
    public bool IsHuman { get; private set; }

    private Profile()
    {
    }

    /// <summary>Factory that controls valid construction: initials are normalized the same way tags are everywhere else.</summary>
    public static Profile Create(string initials, ProfileRole role, string likes, string dislikes, string commonArguments, string philosophy)
    {
        var normalized = Shared.Models.Initials.Normalize(initials);
        if (normalized.Length == 0)
        {
            throw new ArgumentException("Initials need at least one letter or digit.", nameof(initials));
        }

        return new Profile
        {
            Id = ProfileId.From(normalized),
            Role = role,
            Likes = likes,
            Dislikes = dislikes,
            CommonArguments = commonArguments,
            Philosophy = philosophy,
            TtsSettings = TtsSettings.Default(role),
        };
    }

    /// <summary>
    /// The stand-in for a real person taking a side, carrying their fighter tag so the match can be recorded against
    /// them. It is minted per request and never stored — a person's identity lives in the Fighters table, and their
    /// argument style is derived from what they actually say, match after match.
    /// </summary>
    /// <remarks>
    /// Every trait stays neutral and every free-text field empty on purpose: the person is whatever they say into the
    /// microphone, and inventing a personality here would have the AI spouse arguing against a character they never
    /// chose. The prompt is told as much (see the human-opponent directive).
    /// </remarks>
    public static Profile CreateHuman(ProfileRole role, string tag, string? displayName = null) => new()
    {
        Id = ProfileId.From(Shared.Models.Initials.Normalize(tag)),
        Name = string.IsNullOrWhiteSpace(displayName) ? tag : displayName,
        Role = role,
        IsHuman = true,
        TtsSettings = TtsSettings.Default(role),
    };

    public void UpdateFacePic(string? blobName) => FacePic = blobName;

    /// <summary>Full hydration for the repository and the request mapper; the only way to set every field at once.</summary>
    public static Profile Rehydrate(
        ProfileId id,
        string? name,
        int? age,
        string? occupation,
        ProfileRole role,
        string likes,
        string dislikes,
        bool isIntrovert,
        bool isStubborn,
        bool isSpontaneous,
        bool isSarcastic,
        bool isWorkaholic,
        bool isPackRat,
        LoveLanguage? loveLanguage,
        AttachmentStyle? attachmentStyle,
        StressResponse? stressResponse,
        int logicVsEmotion,
        int punctuality,
        int inLawAffinity,
        int screenTime,
        int jealousy,
        int isMessy,
        int spendsMoneyFreely,
        int holdsGrudges,
        int patience,
        string commonArguments,
        string philosophy,
        string? facePic,
        TtsSettings ttsSettings) => new()
        {
            Id = id,
            Name = name,
            Age = age,
            Occupation = occupation,
            Role = role,
            Likes = likes,
            Dislikes = dislikes,
            IsIntrovert = isIntrovert,
            IsStubborn = isStubborn,
            IsSpontaneous = isSpontaneous,
            IsSarcastic = isSarcastic,
            IsWorkaholic = isWorkaholic,
            IsPackRat = isPackRat,
            LoveLanguage = loveLanguage,
            AttachmentStyle = attachmentStyle,
            StressResponse = stressResponse,
            LogicVsEmotion = logicVsEmotion,
            Punctuality = punctuality,
            InLawAffinity = inLawAffinity,
            ScreenTime = screenTime,
            Jealousy = jealousy,
            IsMessy = isMessy,
            SpendsMoneyFreely = spendsMoneyFreely,
            HoldsGrudges = holdsGrudges,
            Patience = patience,
            CommonArguments = commonArguments,
            Philosophy = philosophy,
            FacePic = facePic,
            TtsSettings = ttsSettings,
        };
}

/// <summary>Value object for TTS voice configuration. Voice names are the Gemini prebuilt voices, split by role.</summary>
public sealed record TtsSettings(double Pitch, double Speed, string VoiceName, string? FishReferenceId = null)
{
    private static readonly string[] HusbandVoices = ["Charon", "Puck", "Fenrir"];
    private static readonly string[] WifeVoices = ["Kore", "Zephyr"];

    public static TtsSettings Default(ProfileRole role) =>
        role == ProfileRole.Wife ? new(1.0, 1.0, "Kore") : new(1.0, 1.0, "Charon");

    /// <summary>Keeps the voice inside the allowed set for the role; everything else passes through.</summary>
    public TtsSettings Normalize(ProfileRole role)
    {
        var allowed = role == ProfileRole.Wife ? WifeVoices : HusbandVoices;
        var voice = allowed.Contains(VoiceName, StringComparer.Ordinal) ? VoiceName : Default(role).VoiceName;
        return this with { VoiceName = voice };
    }
}
