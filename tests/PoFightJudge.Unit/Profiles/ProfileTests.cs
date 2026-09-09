using PoFightJudge.Api.Features.Profiles;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Unit.Profiles;

public class ProfileTests
{
    internal static CreateProfileRequest FullRequest(string initials = "abc", ProfileRole role = ProfileRole.Wife) => new()
    {
        Initials = initials,
        Role = role,
        Name = "Alex",
        Age = 41,
        Occupation = "Nurse",
        Likes = "tea",
        Dislikes = "noise",
        IsIntrovert = true,
        IsSarcastic = true,
        LoveLanguage = LoveLanguage.QualityTime,
        AttachmentStyle = AttachmentStyle.Anxious,
        StressResponse = null,
        LogicVsEmotion = 30,
        Punctuality = 90,
        InLawAffinity = 10,
        ScreenTime = 70,
        Jealousy = 20,
        IsMessy = 60,
        SpendsMoneyFreely = 80,
        HoldsGrudges = 95,
        Patience = 15,
        CommonArguments = "the thermostat",
        Philosophy = "say it once, loudly",
        TtsSettings = new TtsSettingsDto { Pitch = 0.9, Speed = 1.1, VoiceName = "Zephyr" },
    };

    [Fact]
    public void Create_normalizes_initials_and_starts_with_the_role_default_voice()
    {
        var profile = Profile.Create("ab1", ProfileRole.Husband, "coffee", "mornings", "budget", "stoic");

        profile.Initials.Should().Be("AB1");
        profile.Id.Should().Be(ProfileId.From("AB1"));
        profile.Role.Should().Be(ProfileRole.Husband);
        profile.TtsSettings.Should().Be(TtsSettings.Default(ProfileRole.Husband));
        profile.IsHuman.Should().BeFalse();
        profile.LogicVsEmotion.Should().Be(50, "sliders start neutral");
    }

    [Fact]
    public void Request_to_domain_to_dto_round_trips_every_field()
    {
        var request = FullRequest();

        var profile = request.ToDomain();
        var dto = profile.ToDto();

        dto.Id.Should().Be(ProfileId.From("ABC"));
        dto.HasFace.Should().BeFalse();
        dto.Persona.Should().BeEquivalentTo(request, o => o.Excluding(r => r.Initials));
        dto.Persona.Initials.Should().Be("ABC", "the DTO carries the stored, normalized initials");

        profile.UpdateFacePic("faces/ABC.png");
        profile.ToDto().HasFace.Should().BeTrue();
    }

    [Fact]
    public void Entity_round_trip_keeps_enums_by_name_and_tts_as_json()
    {
        var profile = FullRequest().ToDomain();
        profile.UpdateFacePic("faces/ABC.png");

        var entity = profile.ToEntity();
        var back = entity.ToDomain();

        entity.PartitionKey.Should().Be(ProfileTableEntity.Partition);
        entity.RowKey.Should().Be("ABC");
        entity.Role.Should().Be("Wife");
        entity.LoveLanguage.Should().Be("QualityTime");
        entity.StressResponse.Should().BeNull();
        entity.TtsSettingsJson.Should().Contain("\"Zephyr\"");
        back.ToDto().Should().BeEquivalentTo(profile.ToDto());
        back.FacePic.Should().Be("faces/ABC.png");
    }
}
