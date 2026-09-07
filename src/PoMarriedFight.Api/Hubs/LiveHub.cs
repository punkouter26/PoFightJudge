using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PoMarriedFight.Api.Features.Auth;
using PoMarriedFight.Api.Features.Fight;
using PoMarriedFight.Shared.Identifiers;
using PoMarriedFight.Shared.Models;

namespace PoMarriedFight.Api.Hubs;

/// <summary>
/// The channel between the room and the fight: microphone frames up, the host's voice, captions and snapshots down.
/// </summary>
[Authorize]
public sealed class LiveHub(SessionRegistry fights, TimeProvider clock) : Hub
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

        Context.Items[FightKey] = id;
        fight.ClientConnected();
        await Groups.AddToGroupAsync(Context.ConnectionId, id.Value);
        await Clients.Caller.SendAsync(LiveHubContract.Snapshot, fight.Session.Snapshot(clock.GetUtcNow()));

        // Only now: the host's opening line must not be said to an empty room.
        await fight.BeginShowAsync(Context.ConnectionAborted);
    }

    [HubMethodName(LiveHubContract.AudioIn)]
    public Task AudioInAsync(byte[] pcm16k) =>
        Current() is { } fight ? fight.OnAudioInAsync(pcm16k, Context.ConnectionAborted) : Task.CompletedTask;

    [HubMethodName(LiveHubContract.EndFight)]
    public async Task EndFightAsync()
    {
        if (Context.Items[FightKey] is MatchId id)
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

    private DebateOrchestrator? Current() => Context.Items[FightKey] is MatchId id ? fights.Get(id) : null;
}
