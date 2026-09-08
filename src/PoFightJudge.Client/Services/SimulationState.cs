using PoFightJudge.Shared.Models;

namespace PoFightJudge.Client.Services;

/// <summary>
/// The matchup chosen on the setup screen, carried to the play screen. It is deliberately not in the URL: a side can
/// be a person whose tag is theirs to keep, and a shared link that silently casts someone else's tag into a match
/// would put words in their mouth and a result on their record.
/// </summary>
public sealed class SimulationState
{
    public MatchSide? Husband { get; private set; }

    public MatchSide? Wife { get; private set; }

    public string Topic { get; private set; } = string.Empty;

    /// <summary>True when a matchup has been chosen this session; the play screen sends the visitor back to setup otherwise.</summary>
    public bool IsReady => Husband is not null && Wife is not null;

    /// <summary>The side a real person is arguing, if any. At most one in a watch — two people is a fight.</summary>
    public MatchSide? Human => Husband is { IsHuman: true } ? Husband : Wife is { IsHuman: true } ? Wife : null;

    public void Set(MatchSide husband, MatchSide wife, string? topic)
    {
        Husband = husband;
        Wife = wife;
        Topic = topic?.Trim() ?? string.Empty;
    }

    public void Clear()
    {
        Husband = null;
        Wife = null;
        Topic = string.Empty;
    }
}
