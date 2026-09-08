using Azure;
using Azure.Data.Tables;

namespace PoMarriedFight.Api.Features.Profiles;

/// <summary>Table Storage row for a <see cref="Profile"/>: PartitionKey <see cref="Partition"/>, RowKey = initials.</summary>
public sealed class ProfileTableEntity : ITableEntity
{
    /// <summary>All profiles share one partition — the list is small and a single-partition query is the common read.</summary>
    public const string Partition = "profile";

    public string PartitionKey { get; set; } = Partition;

    public string RowKey { get; set; } = string.Empty;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public string? Name { get; set; }

    public int? Age { get; set; }

    public string? Occupation { get; set; }

    public string Role { get; set; } = "Husband";

    public string? Likes { get; set; }

    public string? Dislikes { get; set; }

    public bool IsIntrovert { get; set; }

    public bool IsStubborn { get; set; }

    public bool IsSpontaneous { get; set; }

    public bool IsSarcastic { get; set; }

    public bool IsWorkaholic { get; set; }

    public bool IsPackRat { get; set; }

    public string? LoveLanguage { get; set; }

    public string? AttachmentStyle { get; set; }

    public string? StressResponse { get; set; }

    public int LogicVsEmotion { get; set; } = 50;

    public int Punctuality { get; set; } = 50;

    public int InLawAffinity { get; set; } = 50;

    public int ScreenTime { get; set; } = 50;

    public int Jealousy { get; set; } = 50;

    public int IsMessy { get; set; } = 50;

    public int SpendsMoneyFreely { get; set; } = 50;

    public int HoldsGrudges { get; set; } = 50;

    public int Patience { get; set; } = 50;

    public string? CommonArguments { get; set; }

    public string? Philosophy { get; set; }

    public string? FacePic { get; set; }

    public string? TtsSettingsJson { get; set; }

    /// <summary>See <see cref="Profile.FromFights"/>.</summary>
    public bool FromFights { get; set; }
}
