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
        LogicVsEmotion = 30,
        Jealousy = 20,
        HoldsGrudges = 95,
        Patience = 15,
        CommonArguments = "the thermostat",
        Philosophy = "say it once, loudly",
        TtsSettings = new TtsSettingsDto { Pitch = 0.9, Speed = 1.1, VoiceName = "Zephyr" },
    };

    [Fact]
    public void Create_normalizes_initials_and_a_request_round_trips_every_field_through_the_dto_and_the_entity()
    {
        var created = Profile.Create("ab1", ProfileRole.Husband, "coffee", "mornings", "budget", "stoic");

        created.Initials.Should().Be("AB1");
        created.Id.Should().Be(ProfileId.From("AB1"));
        created.Role.Should().Be(ProfileRole.Husband);
        created.TtsSettings.Should().Be(TtsSettings.Default(ProfileRole.Husband), "it starts with the role's default voice");
        created.IsHuman.Should().BeFalse();
        created.LogicVsEmotion.Should().Be(50, "sliders start neutral");

        var request = FullRequest();

        var profile = request.ToDomain();
        var dto = profile.ToDto();

        dto.Id.Should().Be(ProfileId.From("ABC"));
        dto.HasFace.Should().BeFalse();
        dto.Persona.Should().BeEquivalentTo(request, o => o.Excluding(r => r.Initials));
        dto.Persona.Initials.Should().Be("ABC", "the DTO carries the stored, normalized initials");

        profile.UpdateFacePic("faces/ABC.png");
        profile.ToDto().HasFace.Should().BeTrue();

        // The entity keeps the role by name and the voice as JSON.
        var entity = profile.ToEntity();
        var back = entity.ToDomain();

        entity.PartitionKey.Should().Be(ProfileTableEntity.Partition);
        entity.RowKey.Should().Be("ABC");
        entity.Role.Should().Be("Wife");
        entity.TtsSettingsJson.Should().Contain("\"Zephyr\"");
        back.ToDto().Should().BeEquivalentTo(profile.ToDto());
        back.FacePic.Should().Be("faces/ABC.png");
    }
}
