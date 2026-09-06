using FluentValidation;
using PoMarriedFight.Shared.Models;

namespace PoMarriedFight.Shared.Validators;

/// <summary>
/// The one set of rules for a persona, run by the API (400 with field errors) and by the editor form before it posts.
/// Limits are public so the form can set <c>MaxLength</c> on its inputs from the same numbers.
/// </summary>
public sealed class CreateProfileRequestValidator : AbstractValidator<CreateProfileRequest>
{
    public const int MaxNameLength = 60;
    public const int MaxOccupationLength = 80;
    public const int MaxTextLength = 600;
    public const int MinAge = 18;
    public const int MaxAge = 120;
    public const double MinProsody = 0.5;
    public const double MaxProsody = 2.0;
    public const int MaxVoiceNameLength = 40;
    public const int MaxFishReferenceLength = 64;

    public CreateProfileRequestValidator()
    {
        RuleFor(r => r.Initials)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Initials are required.")
            .Must(v => Initials.IsValid(v) && Initials.Normalize(v).Length == v.Trim().Length)
            .WithMessage($"Initials are 1–{Initials.MaxLength} letters or digits.");

        RuleFor(r => r.Role).IsInEnum();
        RuleFor(r => r.Name).MaximumLength(MaxNameLength);
        RuleFor(r => r.Age).InclusiveBetween(MinAge, MaxAge).When(r => r.Age.HasValue);
        RuleFor(r => r.Occupation).MaximumLength(MaxOccupationLength);

        RuleFor(r => r.Likes).NotEmpty().MaximumLength(MaxTextLength);
        RuleFor(r => r.Dislikes).NotEmpty().MaximumLength(MaxTextLength);
        RuleFor(r => r.CommonArguments).MaximumLength(MaxTextLength);
        RuleFor(r => r.Philosophy).MaximumLength(MaxTextLength);

        RuleFor(r => r.LoveLanguage).IsInEnum().When(r => r.LoveLanguage.HasValue);
        RuleFor(r => r.AttachmentStyle).IsInEnum().When(r => r.AttachmentStyle.HasValue);
        RuleFor(r => r.StressResponse).IsInEnum().When(r => r.StressResponse.HasValue);

        RuleFor(r => r.LogicVsEmotion).InclusiveBetween(0, 100);
        RuleFor(r => r.Punctuality).InclusiveBetween(0, 100);
        RuleFor(r => r.InLawAffinity).InclusiveBetween(0, 100);
        RuleFor(r => r.ScreenTime).InclusiveBetween(0, 100);
        RuleFor(r => r.Jealousy).InclusiveBetween(0, 100);
        RuleFor(r => r.IsMessy).InclusiveBetween(0, 100);
        RuleFor(r => r.SpendsMoneyFreely).InclusiveBetween(0, 100);
        RuleFor(r => r.HoldsGrudges).InclusiveBetween(0, 100);
        RuleFor(r => r.Patience).InclusiveBetween(0, 100);

        RuleFor(r => r.TtsSettings).NotNull().SetValidator(new TtsSettingsDtoValidator());
    }
}

public sealed class TtsSettingsDtoValidator : AbstractValidator<TtsSettingsDto>
{
    public TtsSettingsDtoValidator()
    {
        RuleFor(t => t.Pitch).InclusiveBetween(CreateProfileRequestValidator.MinProsody, CreateProfileRequestValidator.MaxProsody);
        RuleFor(t => t.Speed).InclusiveBetween(CreateProfileRequestValidator.MinProsody, CreateProfileRequestValidator.MaxProsody);
        RuleFor(t => t.VoiceName).NotEmpty().MaximumLength(CreateProfileRequestValidator.MaxVoiceNameLength);
        RuleFor(t => t.FishReferenceId)
            .MaximumLength(CreateProfileRequestValidator.MaxFishReferenceLength)
            .Must(v => v!.All(char.IsAsciiLetterOrDigit)).WithMessage("A Fish reference id is letters and digits only.")
            .When(t => !string.IsNullOrEmpty(t.FishReferenceId));
    }
}
