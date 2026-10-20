using FluentValidation;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Shared.Validators;

/// <summary>
/// The bounds on what a watch may send. The client holds the whole match and posts it back a line at a time, so
/// everything here is text somebody else chose on its way into a prompt, a voice call and a table row: each has a
/// length it cannot exceed, and none of them may be missing.
/// </summary>
public static class WatchLimits
{
    /// <summary>One line of an argument. The box a person types into stops at 600; a generated line is shorter.</summary>
    public const int MaxLineLength = 1_000;

    public const int MaxTopicLength = 200;
    public const int MaxInterjectionLength = 300;
    public const int MaxNameLength = 60;
    public const int MaxIdLength = 12;
    public const int MaxMoodLength = 40;

    /// <summary>One line's audio as base64. A fifteen-second line is about a megabyte of PCM; this is four.</summary>
    public const int MaxAudioBase64Length = 4 * 1024 * 1024;
}

public sealed class MatchSideValidator : AbstractValidator<MatchSide>
{
    public MatchSideValidator()
    {
        RuleFor(s => s.Id).NotEmpty().MaximumLength(WatchLimits.MaxIdLength);
        // A person plays under exactly three letters; the cast's ids are the seed's and the server's own.
        RuleFor(s => s.Id).Must(Initials.IsTag).When(s => s.IsHuman).WithMessage("A tag is exactly three letters.");
        RuleFor(s => s.DisplayName).NotNull().MaximumLength(WatchLimits.MaxNameLength);
        RuleFor(s => s.Kind).IsInEnum();
    }
}

public sealed class WatchRoundValidator : AbstractValidator<WatchRoundDto>
{
    public WatchRoundValidator()
    {
        RuleFor(r => r.Speaker).Must(WatchTurns.IsSpeaker).WithMessage("A line belongs to one of the two sides.");
        RuleFor(r => r.Text).NotNull().MaximumLength(WatchLimits.MaxLineLength);
        RuleFor(r => r.Mood).MaximumLength(WatchLimits.MaxMoodLength);

        // Checked before the judge is paid: a clip that will not decode fails the save afterwards, and by then the
        // ruling has been bought and nothing is kept.
        RuleFor(r => r.Audio!.Base64)
            .MaximumLength(WatchLimits.MaxAudioBase64Length)
            .Must(IsBase64).WithMessage("The line's audio is not valid base64.")
            .When(r => r.Audio is { IsEmpty: false });
    }

    private static bool IsBase64(string value) =>
        Convert.TryFromBase64String(value, new byte[((value.Length + 3) / 4) * 3], out _);
}

public sealed class GenerateRoundRequestValidator : AbstractValidator<GenerateRoundRequest>
{
    public GenerateRoundRequestValidator()
    {
        RuleFor(r => r.Husband).NotNull().SetValidator(new MatchSideValidator());
        RuleFor(r => r.Wife).NotNull().SetValidator(new MatchSideValidator());
        RuleFor(r => r.History).NotNull();
        RuleForEach(r => r.History).NotNull().SetValidator(new WatchRoundValidator());
        RuleFor(r => r.Topic).MaximumLength(WatchLimits.MaxTopicLength);
        RuleFor(r => r.Interjection).MaximumLength(WatchLimits.MaxInterjectionLength);
    }
}

public sealed class RoundAudioRequestValidator : AbstractValidator<RoundAudioRequest>
{
    public RoundAudioRequestValidator() =>
        RuleFor(r => r.Text).NotEmpty().MaximumLength(WatchLimits.MaxLineLength);
}

public sealed class VerdictRequestValidator : AbstractValidator<VerdictRequest>
{
    public VerdictRequestValidator()
    {
        RuleFor(r => r.Husband).NotNull().SetValidator(new MatchSideValidator());
        RuleFor(r => r.Wife).NotNull().SetValidator(new MatchSideValidator());
        RuleFor(r => r.Rounds).NotNull();
        RuleForEach(r => r.Rounds).NotNull().SetValidator(new WatchRoundValidator());
        RuleFor(r => r.Topic).MaximumLength(WatchLimits.MaxTopicLength);
    }
}

public sealed class RenameFighterRequestValidator : AbstractValidator<RenameFighterRequest>
{
    public RenameFighterRequestValidator() =>
        RuleFor(r => r.DisplayName)
            .MaximumLength(WatchLimits.MaxNameLength)
            .Must(name => name is null || !name.Any(char.IsControl)).WithMessage("A name is one line of ordinary text.");
}
