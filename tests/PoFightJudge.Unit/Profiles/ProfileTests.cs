using System.Text.Json;
using PoFightJudge.Api.Features.Profiles;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Unit.Profiles;

public class ProfileTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

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

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!")]
    public void Create_rejects_initials_without_a_letter_or_digit(string initials)
    {
        var act = () => Profile.Create(initials, ProfileRole.Wife, "a", "b", "c", "d");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_human_side_carries_its_tag_and_stays_deliberately_blank()
    {
        var human = Profile.CreateHuman(ProfileRole.Wife, "kd", "Kim");

        human.IsHuman.Should().BeTrue();
        human.Initials.Should().Be("KD", "the fighter tag is the identity, so the match can be recorded against them");
        human.Name.Should().Be("Kim");
        human.Likes.Should().BeEmpty("a person is whatever they say into the microphone, not an invented personality");
        human.Patience.Should().Be(50);
        human.TtsSettings.VoiceName.Should().Be("Kore");
        Profile.CreateHuman(ProfileRole.Husband, "AB").Name.Should().Be("AB", "the tag stands in until they name themselves");
    }

    [Fact]
    public void Tts_normalize_keeps_the_voice_inside_the_role_set()
    {
        var wifeWithHusbandVoice = new TtsSettings(1.0, 1.0, "Charon").Normalize(ProfileRole.Wife);
        var husbandWithValidVoice = new TtsSettings(0.8, 1.2, "Puck").Normalize(ProfileRole.Husband);

        wifeWithHusbandVoice.VoiceName.Should().Be("Kore");
        husbandWithValidVoice.Should().Be(new TtsSettings(0.8, 1.2, "Puck"));
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

    [Fact]
    public void Entity_with_legacy_blanks_rehydrates_with_defaults()
    {
        var entity = new ProfileTableEntity { PartitionKey = ProfileTableEntity.Partition, RowKey = "OLD", Role = "husband", TtsSettingsJson = null, LoveLanguage = "" };

        var profile = entity.ToDomain();

        profile.Role.Should().Be(ProfileRole.Husband, "role names parse case-insensitively");
        profile.TtsSettings.Should().Be(TtsSettings.Default(ProfileRole.Husband));
        profile.LoveLanguage.Should().BeNull();
        profile.Likes.Should().BeEmpty();
    }

    [Fact]
    public void Profile_enums_travel_as_strings_on_the_wire()
    {
        var json = JsonSerializer.Serialize(FullRequest(), Web);

        json.Should().Contain("\"role\":\"Wife\"", "the default web options are camelCase + the enum's own converter")
            .And.Contain("\"loveLanguage\":\"QualityTime\"")
            .And.Contain("\"stressResponse\":null");
        JsonSerializer.Deserialize<CreateProfileRequest>(json, Web)!.AttachmentStyle.Should().Be(AttachmentStyle.Anxious);
    }
    [Fact]
    public void A_persona_read_from_fights_says_so_all_the_way_through_storage_and_out_to_the_client()
    {
        var profile = FullRequest("KKK", ProfileRole.Husband).ToDomain();
        profile.FromFights.Should().BeFalse("anything that came through the editor was authored");

        profile.MarkFromFights();

        profile.ToEntity().FromFights.Should().BeTrue();
        profile.ToEntity().ToDomain().FromFights.Should().BeTrue("or a restart would turn it back into a cast member");
        profile.ToDto().FromFights.Should().BeTrue("the cast page marks these apart");
        profile.ToRequest().ToDomain().FromFights.Should().BeFalse("the flag is not the editor's to set");
    }
}
