using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using PoFightJudge.Shared;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Client.Services;

/// <summary>
/// Reconnects for as long as the page is open, settling into a steady retry rather than giving up. SignalR's
/// default policy stops after 0s, 2s, 10s and 30s, which leaves the page permanently dead — and silently so —
/// whenever the API takes longer than half a minute to come back.
/// </summary>
public sealed class ForeverRetryPolicy : IRetryPolicy
{
    private static readonly TimeSpan[] Ramp =
    [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
    ];

    /// <summary>The interval it settles on once the opening ramp is exhausted.</summary>
    public static TimeSpan SteadyInterval { get; } = TimeSpan.FromSeconds(10);

    public TimeSpan? NextRetryDelay(RetryContext retryContext)
    {
        ArgumentNullException.ThrowIfNull(retryContext);
        return retryContext.PreviousRetryCount < Ramp.Length ? Ramp[retryContext.PreviousRetryCount] : SteadyInterval;
    }
}

/// <summary>
/// What the room does with everything a fight sends it. One seam rather than eight events: it mirrors the server's
/// own sink, and the page that shows a fight implements all of it anyway.
/// </summary>
public interface ILiveFightListener
{
    /// <summary>A chunk of the host's voice, 24 kHz PCM.</summary>
    void OnHostAudio(byte[] pcm24k);

    /// <summary>The host was cut off: stop playing what is queued.</summary>
    void OnHostInterrupted();

    void OnCaption(CaptionDto caption);

    void OnSnapshot(DebateSnapshotDto snapshot);

    void OnEnded(string matchId);

    /// <summary>Something went wrong that the room should know about, without the fight ending.</summary>
    void OnProblem(string message);

    /// <summary>The connection dropped and is being retried.</summary>
    void OnConnectionLost(string? reason);

    /// <summary>Retrying has stopped for good; only starting again will help.</summary>
    void OnConnectionAbandoned(string reason);
}

/// <summary>
/// The browser's end of one live fight. Microphone frames go up through <see cref="IAudioFrameSink"/>; everything
/// the room hears or reads comes back through the listener.
/// </summary>
public sealed class LiveConnection(Uri baseAddress, IAccessTokenProvider? tokens) : IAudioFrameSink, IAsyncDisposable
{
    private HubConnection? _hub;
    private ILiveFightListener? _listener;
    private MatchId? _matchId;
    private bool _stopped;

    public bool IsConnected => _hub is { State: HubConnectionState.Connected };

    public async Task ConnectAsync(MatchId matchId, ILiveFightListener listener, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(listener);

        _matchId = matchId;
        _listener = listener;
        _hub = new HubConnectionBuilder()
            .WithUrl(new Uri(baseAddress, ApiRoutes.Hubs.Live), options =>
            {
                if (tokens is not null)
                {
                    options.AccessTokenProvider = async () =>
                    {
                        var result = await tokens.RequestAccessToken();
                        return result.TryGetToken(out var token) ? token.Value : null;
                    };
                }
            })

            // Frames and host audio are raw PCM; MessagePack sends them as bytes rather than as base64 text.
            .AddMessagePackProtocol()
            .WithAutomaticReconnect(new ForeverRetryPolicy())
            .Build();

        _hub.On<byte[]>(LiveHubContract.AudioOut, pcm => listener.OnHostAudio(pcm));
        _hub.On(LiveHubContract.AudioClear, listener.OnHostInterrupted);
        _hub.On<CaptionDto>(LiveHubContract.Caption, listener.OnCaption);
        _hub.On<DebateSnapshotDto>(LiveHubContract.Snapshot, listener.OnSnapshot);
        _hub.On<string>(LiveHubContract.Ended, listener.OnEnded);
        _hub.On<string>(LiveHubContract.Error, listener.OnProblem);

        _hub.Reconnecting += ex =>
        {
            listener.OnConnectionLost(ex?.Message);
            return Task.CompletedTask;
        };

        _hub.Reconnected += async _ =>
        {
            // A reconnect brings a new connection id, so the fight's group membership has to be re-established.
            if (_matchId is not { } id)
            {
                return;
            }

            try
            {
                await _hub.InvokeAsync(LiveHubContract.JoinFight, id.Value);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Fights live in memory, so a server restart loses them: the socket is back but this fight is not.
                // Without this the rejoin faults on a handler nobody awaits and the page claims it is still trying.
                listener.OnConnectionAbandoned(ex is HubException
                    ? "This fight ended when the server restarted, and its recording went with it. Start another one."
                    : $"Rejoining the fight failed: {ex.Message}");
            }
        };

        _hub.Closed += ex =>
        {
            // With the forever policy this only fires once the connection has stopped for good.
            if (!_stopped)
            {
                listener.OnConnectionAbandoned(ex?.Message ?? "The connection to the fight closed.");
            }

            return Task.CompletedTask;
        };

        await _hub.StartAsync(ct);
        await _hub.InvokeAsync(LiveHubContract.JoinFight, matchId.Value, ct);
    }

    /// <summary>A frame is dropped rather than queued while the connection is down: it would be stale by the time it landed.</summary>
    public Task OnAudioFrameAsync(byte[] pcm16k) =>
        IsConnected ? _hub!.SendAsync(LiveHubContract.AudioIn, pcm16k) : Task.CompletedTask;

    public Task EndFightAsync() => IsConnected ? _hub!.InvokeAsync(LiveHubContract.EndFight) : Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        _stopped = true;
        _listener = null;
        if (_hub is not null)
        {
            await _hub.DisposeAsync();
        }
    }
}

/// <summary>Makes one connection per fight, carrying whatever token the browser is signed in with.</summary>
public sealed class LiveConnectionFactory(IServiceProvider services, Uri baseAddress)
{
    public LiveConnection Create()
    {
        ArgumentNullException.ThrowIfNull(services);
        return new LiveConnection(baseAddress, services.GetService<IAccessTokenProvider>());
    }
}
