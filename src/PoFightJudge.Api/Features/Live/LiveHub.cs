using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PoFightJudge.Api.Features.Auth;
using PoFightJudge.Api.Features.Fight;
using PoFightJudge.Api.Features.Records;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Api.Features.Live;

/// <summary>
/// The channel between the room and the fight: microphone frames up, the host's voice, captions and snapshots down.
/// </summary>
[Authorize]
public sealed class LiveHub(SessionRegistry fights, IMatchRepository matches, TimeProvider clock) : Hub
{
    private const string FightKey = "matchId";

    [HubMethodName(LiveHubContract.JoinFight)]
    public async Task JoinFightAsync(string matchId)
    {
        if (!MatchId.TryParse(matchId, null, out var id))
        {
            throw new HubException("That is not a fight id.");
        }

        // Tenancy is checked here and nowhere else: joining is the only way into a fight's group.
        var fight = fights.Get(id, Context.User?.UserIdOrNull()) ?? throw new HubException("Fight not found.");

        // Counted once per connection: a second JoinFight on the same one — a reconnect that re-joins — would otherwise
        // leave the count above zero for good, and "nobody is in the room" would never be true again.
        if (Joined() != id)
        {
            Context.Items[FightKey] = id;
            fight.ClientConnected();
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, id.Value);
        await Clients.Caller.SendAsync(LiveHubContract.Snapshot, fight.Session.Snapshot(clock.GetUtcNow()));

        // Only now: the host's opening line must not be said to an empty room.
        await fight.BeginShowAsync(Context.ConnectionAborted);
    }

    /// <summary>The group that hears how the reading of one fight is going. Separate from the fight's own group: the room is gone by then.</summary>
    public static string AnalysisGroup(MatchId id) => $"analysis:{id.Value}";

    /// <summary>
    /// Puts this connection on the list for one fight's reading. The fight has to be the caller's own: the group
    /// name is guessable, and the only thing that keeps a stranger out of it is this check.
    /// </summary>
    [HubMethodName(LiveHubContract.WatchAnalysis)]
    public async Task WatchAnalysisAsync(string matchId)
    {
        if (!MatchId.TryParse(matchId, null, out var id)
            || Context.User?.UserIdOrNull() is not { } userId
            || await matches.GetAsync(userId, id, Context.ConnectionAborted) is null)
        {
            throw new HubException("Fight not found.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, AnalysisGroup(id));
    }

    [HubMethodName(LiveHubContract.AudioIn)]
    public Task AudioInAsync(byte[] pcm16k) =>
        Current() is { } fight ? fight.OnAudioInAsync(pcm16k, Context.ConnectionAborted) : Task.CompletedTask;

    [HubMethodName(LiveHubContract.EndFight)]
    public async Task EndFightAsync()
    {
        if (Joined() is { } id)
        {
            await fights.EndAsync(id, "ended from the room", CancellationToken.None);
        }
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        // The fight ends itself after the client grace elapses; a SignalR auto-reconnect re-joins well before that.
        Current()?.ClientDisconnected();
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>The fight this connection joined, if it joined one. The indexer throws on a missing key, so it is not used.</summary>
    private MatchId? Joined() => Context.Items.TryGetValue(FightKey, out var value) && value is MatchId id ? id : null;

    private DebateOrchestrator? Current() => Joined() is { } id ? fights.Get(id) : null;
}
