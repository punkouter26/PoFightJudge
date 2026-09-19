using FluentValidation.TestHelper;
using PoFightJudge.Shared.Models;
using PoFightJudge.Shared.Validators;

namespace PoFightJudge.Unit.Profiles;

public class CreateProfileRequestValidatorTests
{
    private readonly CreateProfileRequestValidator _sut = new();

    [Fact]
    public void A_complete_persona_passes()
    {
        _sut.TestValidate(ProfileTests.FullRequest()).ShouldNotHaveAnyValidationErrors();
        _sut.TestValidate(new CreateProfileRequest { Initials = "A", Likes = "x", Dislikes = "y" }).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Each_rule_names_its_field()
    {
        (string Property, Action<CreateProfileRequest> Mutate)[] invalidCases =
        [
            (nameof(CreateProfileRequest.Initials), r => r.Initials = ""),
            (nameof(CreateProfileRequest.Initials), r => r.Initials = "ABCD"),
            (nameof(CreateProfileRequest.Initials), r => r.Initials = "A-B"),
            (nameof(CreateProfileRequest.Role), r => r.Role = (ProfileRole)42),
            (nameof(CreateProfileRequest.Name), r => r.Name = new string('n', CreateProfileRequestValidator.MaxNameLength + 1)),
            (nameof(CreateProfileRequest.Age), r => r.Age = 17),
            (nameof(CreateProfileRequest.Age), r => r.Age = 121),
            (nameof(CreateProfileRequest.Likes), r => r.Likes = ""),
            (nameof(CreateProfileRequest.Dislikes), r => r.Dislikes = " "),
            (nameof(CreateProfileRequest.Philosophy), r => r.Philosophy = new string('p', CreateProfileRequestValidator.MaxTextLength + 1)),
            (nameof(CreateProfileRequest.Patience), r => r.Patience = 101),
            (nameof(CreateProfileRequest.Jealousy), r => r.Jealousy = -1),
            ($"{nameof(CreateProfileRequest.TtsSettings)}.{nameof(TtsSettingsDto.Pitch)}", r => r.TtsSettings.Pitch = 2.5),
            ($"{nameof(CreateProfileRequest.TtsSettings)}.{nameof(TtsSettingsDto.Speed)}", r => r.TtsSettings.Speed = 0.1),
            ($"{nameof(CreateProfileRequest.TtsSettings)}.{nameof(TtsSettingsDto.VoiceName)}", r => r.TtsSettings.VoiceName = ""),
        ];

        foreach (var (property, mutate) in invalidCases)
        {
            var request = ProfileTests.FullRequest();
            mutate(request);

            var result = _sut.TestValidate(request);
            result.ShouldHaveValidationErrorFor(property);
            result.Errors.Should().ContainSingle("one message per broken field keeps the form readable");
        }
    }
}
