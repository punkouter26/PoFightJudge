using Vogen;

namespace PoMarriedFight.Shared.Identifiers;

/// <summary>A match (WATCH game or FIGHT session) id: a compact lower-case GUID, the Matches RowKey and the blob folder name.</summary>
[ValueObject<string>(conversions: Conversions.SystemTextJson | Conversions.TypeConverter, comparison: ComparisonGeneration.Omit)]
public readonly partial struct MatchId
{
    public static MatchId New() => From(Guid.NewGuid().ToString("N"));

    private static string NormalizeInput(string input) => input.Trim().ToLowerInvariant();

    private static Validation Validate(string value) =>
        value.Length == 32 && value.All(char.IsAsciiHexDigitLower)
            ? Validation.Ok
            : Validation.Invalid("A match id is a 32-character hex string.");
}
