using PoMarriedFight.Shared.Identifiers;
using PoMarriedFight.Shared.Models;

namespace PoMarriedFight.Api.Features.Fighters;

/// <summary>
/// A real person who has argued at least once, in either mode. Nobody writes a Fighter: it appears the first time
/// its tag speaks, and everything that makes it interesting — record, badges, rivalries, form, argument style — is
/// derived on read from the results of the debates they spoke in. The tag is the identity; the display name is the
/// only thing anyone may edit, so a person cannot rename themselves out of their own record.
/// </summary>
public sealed class Fighter
{
    private Fighter()
    {
    }

    public FighterId Id { get; private set; }

    public string Tag => Id.Value;

    /// <summary>What to call them. Defaults to the tag until they choose something.</summary>
    public string DisplayName { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When this tag last argued. Moves on every match, so an idle roster can be read at a glance.</summary>
    public DateTimeOffset LastSeenAt { get; private set; }

    public static Fighter Create(FighterId id, DateTimeOffset now, string? displayName = null) => new()
    {
        Id = id,
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? id.Value : displayName.Trim(),
        CreatedAt = now,
        LastSeenAt = now,
    };

    public static Fighter Rehydrate(FighterDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        return new Fighter
        {
            Id = FighterId.From(dto.Tag),
            DisplayName = string.IsNullOrWhiteSpace(dto.DisplayName) ? dto.Tag : dto.DisplayName,
            CreatedAt = dto.CreatedAt,
            LastSeenAt = dto.LastSeenAt,
        };
    }

    /// <summary>The only editable field. A blank name falls back to the tag rather than leaving them nameless.</summary>
    public void Rename(string? displayName) => DisplayName = string.IsNullOrWhiteSpace(displayName) ? Tag : displayName.Trim();

    /// <summary>Records that this tag argued again. Never moves backwards, so an out-of-order write cannot rewind the roster.</summary>
    public void Seen(DateTimeOffset at)
    {
        if (at > LastSeenAt)
        {
            LastSeenAt = at;
        }
    }

    public FighterDto ToDto() => new(Tag, DisplayName, CreatedAt, LastSeenAt);
}
