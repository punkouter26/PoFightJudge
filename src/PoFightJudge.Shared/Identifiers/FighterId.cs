using PoFightJudge.Shared.Models;
using Vogen;

namespace PoFightJudge.Shared.Identifiers;

/// <summary>
/// A real fighter's tag, upper case. A new one is exactly three letters (<see cref="Initials.IsTag"/>, checked where it is
/// entered); reading accepts the 1–3 alphanumerics older tags were made with. Auto-created on first fight;
/// the FightResults partition key.
/// </summary>
[ValueObject<string>(conversions: Conversions.SystemTextJson | Conversions.TypeConverter, comparison: ComparisonGeneration.Omit)]
public readonly partial struct FighterId
{
    private static string NormalizeInput(string input) => Initials.Normalize(input);

    private static Validation Validate(string value) =>
        Initials.IsValid(value) && value.Length <= Initials.MaxLength
            ? Validation.Ok
            : Validation.Invalid("A fighter tag is 1–3 letters or digits.");
}
