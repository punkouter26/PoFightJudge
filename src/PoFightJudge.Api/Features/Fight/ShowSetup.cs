using PoFightJudge.Shared.Models;

namespace PoFightJudge.Api.Features.Fight;

/// <summary>
/// What was chosen before the microphone went on: the host, both fighter tags, an optional agreed topic, and what
/// is already known about how each of them argues. Validated at the endpoint, so everything downstream can trust
/// it — the tags in particular are never model-supplied, because the record goes on them.
/// </summary>
public sealed record ShowSetup(HostPersonaId Persona, string Player1, string Player2, string? Topic)
{
    /// <summary>No tags, no topic: the referee hosts and asks for everything at the top of the show.</summary>
    public static ShowSetup Default { get; } = new(HostPersonaId.Referee, string.Empty, string.Empty, null);

    /// <summary>
    /// One line on how this fighter has argued before, built from their past fights. Empty for someone the room has
    /// not met; it is put in front of the host so it can hold them to their own habits.
    /// </summary>
    public string? Player1Digest { get; init; }

    public string? Player2Digest { get; init; }

    /// <summary>True when both fighters named themselves up front, so the host must neither invent nor overwrite a name.</summary>
    public bool HasPlayers => Initials.ArePair(Player1, Player2);

    public bool HasTopic => !string.IsNullOrWhiteSpace(Topic);
}
