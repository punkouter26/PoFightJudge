using PoMarriedFight.Shared.Identifiers;

namespace PoMarriedFight.Shared;

/// <summary>
/// The reserved "SELF" participant in WATCH — the live human who argues through the microphone instead of being
/// generated. Deliberately not a stored profile: no row, no personality, no record. A SELF match is an exhibition.
/// </summary>
public static class SelfPlayer
{
    /// <summary>The reserved initials. <see cref="ProfileId"/> upper-cases, so this is the exact stored value.</summary>
    public const string Initials = "SELF";

    /// <summary>Display name shown wherever a real profile would show its name.</summary>
    public const string DisplayName = "You";

    public static ProfileId Id => ProfileId.From(Initials);

    public static bool Is(ProfileId id) => string.Equals(id.Value, Initials, StringComparison.Ordinal);

    public static bool Is(string? initials) =>
        !string.IsNullOrWhiteSpace(initials) && string.Equals(initials.Trim(), Initials, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when either side is the human — the game must not be persisted and no record may be written.</summary>
    public static bool InMatch(ProfileId husband, ProfileId wife) => Is(husband) || Is(wife);
}
