using Vogen;

namespace PoMarriedFight.Shared.Identifiers;

/// <summary>
/// A WATCH persona's id — the initials, upper-cased, and also the Table Storage RowKey. 1–4 alphanumerics so the
/// three-letter initials and the reserved <c>SELF</c> sentinel both fit. Vogen generates the STJ converter,
/// <c>IParsable</c> (route binding) and equality.
/// </summary>
[ValueObject<string>(conversions: Conversions.SystemTextJson | Conversions.TypeConverter, comparison: ComparisonGeneration.Omit)]
public readonly partial struct ProfileId
{
    public const int MaxLength = 4;

    private static string NormalizeInput(string input) =>
        new([.. input.ToUpperInvariant().Where(char.IsAsciiLetterOrDigit).Take(MaxLength)]);

    private static Validation Validate(string value) =>
        value.Length is >= 1 and <= MaxLength ? Validation.Ok : Validation.Invalid("A profile id is 1–4 letters or digits.");
}
