using PoFightJudge.Shared.Models;
using Vogen;

namespace PoFightJudge.Shared.Identifiers;

/// <summary>
/// The id of a cast member — a real person or an authored persona, both keyed by their upper-cased initials. The
/// same 1–3 alphanumeric normalisation as <see cref="FighterId"/> so the existing persona initials (HRC, DJT, KDH,
/// …) and the 3-letter fighter tags share one shape. The Table Storage RowKey of the unified
/// <c>castmembers</c> Table.
/// </summary>
[ValueObject<string>(conversions: Conversions.SystemTextJson | Conversions.TypeConverter, comparison: ComparisonGeneration.Omit)]
public readonly partial struct CastMemberId
{
    private static string NormalizeInput(string input) => Initials.Normalize(input);

    private static Validation Validate(string value) =>
        Initials.IsValid(value) && value.Length <= Initials.MaxLength
            ? Validation.Ok
            : Validation.Invalid("A cast member id is 1–3 letters or digits.");
}
