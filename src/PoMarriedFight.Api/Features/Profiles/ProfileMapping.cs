using System.Text.Json;
using PoMarriedFight.Shared.Identifiers;
using PoMarriedFight.Shared.Models;
using Riok.Mapperly.Abstractions;

namespace PoMarriedFight.Api.Features.Profiles;

/// <summary>
/// Profile ↔ wire DTO ↔ table row. Mapperly generates the member-by-member copies (enums by name); the two directions
/// that end in the aggregate are written out because <see cref="Profile"/> only hydrates through
/// <see cref="Profile.Rehydrate"/>.
/// </summary>
[Mapper(EnumMappingStrategy = EnumMappingStrategy.ByName)]
public static partial class ProfileMapping
{
    [MapperIgnoreSource(nameof(Profile.Initials))]
    [MapperIgnoreSource(nameof(Profile.IsSelf))]
    [MapProperty(nameof(Profile.FacePic), nameof(ProfileDto.HasFace), Use = nameof(HasValue))]
    [MapPropertyFromSource(nameof(ProfileDto.Persona))]
    public static partial ProfileDto ToDto(this Profile profile);

    /// <summary>The editable view of a stored persona — what the editor form is seeded with.</summary>
    [MapperIgnoreSource(nameof(Profile.Id))]
    [MapperIgnoreSource(nameof(Profile.IsSelf))]
    [MapperIgnoreSource(nameof(Profile.FacePic))]
    public static partial CreateProfileRequest ToRequest(this Profile profile);

    [MapperIgnoreSource(nameof(Profile.Id))]
    [MapperIgnoreSource(nameof(Profile.IsSelf))]
    [MapperIgnoreTarget(nameof(ProfileTableEntity.Timestamp))]
    [MapperIgnoreTarget(nameof(ProfileTableEntity.ETag))]
    [MapValue(nameof(ProfileTableEntity.PartitionKey), ProfileTableEntity.Partition)]
    [MapProperty(nameof(Profile.Initials), nameof(ProfileTableEntity.RowKey))]
    [MapProperty(nameof(Profile.TtsSettings), nameof(ProfileTableEntity.TtsSettingsJson), Use = nameof(SerializeTts))]
    public static partial ProfileTableEntity ToEntity(this Profile profile);

    public static partial TtsSettings ToDomain(this TtsSettingsDto dto);

    /// <summary>A validated request becomes a persona with the voice coerced into the role set; the face is not part of the request (<see cref="Profile.UpdateFacePic"/>).</summary>
    public static Profile ToDomain(this CreateProfileRequest request) => Profile.Rehydrate(
        ProfileId.From(Initials.Normalize(request.Initials)),
        request.Name,
        request.Age,
        request.Occupation,
        request.Role,
        request.Likes,
        request.Dislikes,
        request.IsIntrovert,
        request.IsStubborn,
        request.IsSpontaneous,
        request.IsSarcastic,
        request.IsWorkaholic,
        request.IsPackRat,
        request.LoveLanguage,
        request.AttachmentStyle,
        request.StressResponse,
        request.LogicVsEmotion,
        request.Punctuality,
        request.InLawAffinity,
        request.ScreenTime,
        request.Jealousy,
        request.IsMessy,
        request.SpendsMoneyFreely,
        request.HoldsGrudges,
        request.Patience,
        request.CommonArguments,
        request.Philosophy,
        facePic: null,
        request.TtsSettings.ToDomain().Normalize(request.Role));

    /// <summary>Rows written by older code may hold blanks or lower-case names; both read back as defaults rather than failing the whole list.</summary>
    public static Profile ToDomain(this ProfileTableEntity entity)
    {
        var role = Enum.TryParse<ProfileRole>(entity.Role, ignoreCase: true, out var parsedRole) ? parsedRole : ProfileRole.Husband;
        var tts = string.IsNullOrEmpty(entity.TtsSettingsJson)
            ? null
            : JsonSerializer.Deserialize<TtsSettings>(entity.TtsSettingsJson);

        return Profile.Rehydrate(
            ProfileId.From(entity.RowKey),
            entity.Name,
            entity.Age,
            entity.Occupation,
            role,
            entity.Likes ?? string.Empty,
            entity.Dislikes ?? string.Empty,
            entity.IsIntrovert,
            entity.IsStubborn,
            entity.IsSpontaneous,
            entity.IsSarcastic,
            entity.IsWorkaholic,
            entity.IsPackRat,
            ParseOrNull<LoveLanguage>(entity.LoveLanguage),
            ParseOrNull<AttachmentStyle>(entity.AttachmentStyle),
            ParseOrNull<StressResponse>(entity.StressResponse),
            entity.LogicVsEmotion,
            entity.Punctuality,
            entity.InLawAffinity,
            entity.ScreenTime,
            entity.Jealousy,
            entity.IsMessy,
            entity.SpendsMoneyFreely,
            entity.HoldsGrudges,
            entity.Patience,
            entity.CommonArguments ?? string.Empty,
            entity.Philosophy ?? string.Empty,
            entity.FacePic,
            tts ?? TtsSettings.Default(role));
    }

    private static bool HasValue(string? value) => !string.IsNullOrEmpty(value);

    private static string SerializeTts(TtsSettings settings) => JsonSerializer.Serialize(settings);

    private static T? ParseOrNull<T>(string? name)
        where T : struct, Enum =>
        !string.IsNullOrEmpty(name) && Enum.TryParse<T>(name, ignoreCase: true, out var value) ? value : null;
}
