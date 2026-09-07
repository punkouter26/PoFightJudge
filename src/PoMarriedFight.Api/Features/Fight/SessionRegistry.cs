using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using PoMarriedFight.Api.Features.Ai;
using PoMarriedFight.Api.Features.Live;
using PoMarriedFight.Api.Features.Records;
using PoMarriedFight.Api.Features.Storage;
using PoMarriedFight.Api.Hubs;
using PoMarriedFight.Shared.Identifiers;
using PoMarriedFight.Shared.Models;

namespace PoMarriedFight.Api.Features.Fight;

/// <summary>Where a fight sends what the room hears. One per fight, because the room is a group of one fight's viewers.</summary>
public delegate ILiveClientSink LiveSinkFactory(MatchId matchId);

/// <summary>
/// The fights running in this process, and the one-second tick that keeps their clocks honest. One live fight per
/// user: a second one ends the first rather than leaving a stale tab recording into a match nobody is watching.
/// </summary>
public sealed partial class SessionRegistry(
    IServiceScopeFactory scopes,
    LiveClientFactory liveFactory,
    LiveSinkFactory sinkFactory,
    IAnalysisIntake analysis,
    DebateOptions options,
    AiMode ai,
    TimeProvider clock,
    ILoggerFactory loggers) : IHostedService, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, LiveFight> _fights = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger<SessionRegistry> _logger = loggers.CreateLogger<SessionRegistry>();
    private Task? _ticker;

    public async Task<DebateOrchestrator> CreateAsync(string userId, ShowSetup setup, CancellationToken ct)
    {
        // One show at a time: a stale tab's fight is ended, and persisted, before the new one starts.
        foreach (var (id, running) in _fights)
        {
            if (string.Equals(running.Orchestrator.UserId, userId, StringComparison.Ordinal))
            {
                await EndAsync(MatchId.Parse(id, null), "replaced by a new fight", ct);
            }
        }

        var matchId = MatchId.New();

        // The repositories are scoped and a fight outlives any request, so it gets a scope of its own for its life.
        var scope = scopes.CreateScope();
        var orchestrator = new DebateOrchestrator(
            userId,
            matchId,
            setup,
            liveFactory,
            sinkFactory(matchId),
            scope.ServiceProvider.GetRequiredService<IMatchRepository>(),
            scope.ServiceProvider.GetRequiredService<IAudioBlobStore>(),
            analysis,
            options,
            clock,
            ai.UseFakes,
            loggers.CreateLogger<DebateOrchestrator>());

        _fights[matchId.Value] = new LiveFight(orchestrator, scope);
        try
        {
            await orchestrator.StartAsync(ct);
        }
        catch
        {
            _fights.TryRemove(matchId.Value, out _);
            await orchestrator.DisposeAsync();
            scope.Dispose();
            throw;
        }

        return orchestrator;
    }

    public DebateOrchestrator? Get(MatchId matchId) => _fights.GetValueOrDefault(matchId.Value)?.Orchestrator;

    /// <summary>The live fight only if it belongs to this user. The one place the tenancy rule lives.</summary>
    public DebateOrchestrator? Get(MatchId matchId, string? userId) =>
        userId is not null && Get(matchId) is { } running && string.Equals(running.UserId, userId, StringComparison.Ordinal)
            ? running
            : null;

    public async Task EndAsync(MatchId matchId, string reason, CancellationToken ct)
    {
        if (_fights.TryRemove(matchId.Value, out var fight))
        {
            try
            {
                await fight.Orchestrator.EndAsync(reason, ct);
            }
            finally
            {
                await fight.DisposeAsync();
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ticker = Task.Run(() => TickLoopAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cts.CancelAsync();
        foreach (var id in _fights.Keys)
        {
            await EndAsync(MatchId.Parse(id, null), "server shutting down", cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_cts.IsCancellationRequested)
        {
            await _cts.CancelAsync();
        }

        _cts.Dispose();
    }

    /// <summary>One pass of the clock over every running fight. Public so a test can drive it without waiting a second.</summary>
    public async Task TickAllAsync(CancellationToken ct)
    {
        foreach (var (id, fight) in _fights)
        {
            // Reaped only once persistence has finished: an orchestrator mid-end must not be disposed underneath itself.
            if (fight.Orchestrator.Finished)
            {
                if (_fights.TryRemove(id, out var done))
                {
                    await done.DisposeAsync();
                }

                continue;
            }

            if (fight.Orchestrator.Ended)
            {
                continue;
            }

            try
            {
                await fight.Orchestrator.TickAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogTickFailed(_logger, id, ex);
            }
        }
    }

    private async Task TickLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), clock);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                await TickAllAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    [LoggerMessage(EventId = 4101, Level = LogLevel.Warning, Message = "Tick failed for fight {MatchId}")]
    private static partial void LogTickFailed(ILogger logger, string matchId, Exception ex);

    /// <summary>A running fight and the service scope it borrowed for its lifetime.</summary>
    private sealed record LiveFight(DebateOrchestrator Orchestrator, IServiceScope Scope) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Orchestrator.DisposeAsync();
            Scope.Dispose();
        }
    }
}

/// <summary>The sink over the SignalR group for one fight: everyone watching it is in a group named by its id.</summary>
public sealed class HubClientSink(IHubContext<LiveHub> hub, MatchId matchId) : ILiveClientSink
{
    private IClientProxy Group => hub.Clients.Group(matchId.Value);

    public Task AudioOutAsync(ReadOnlyMemory<byte> pcm24k, CancellationToken ct) =>
        Group.SendAsync(LiveHubContract.AudioOut, pcm24k.ToArray(), ct);

    public Task AudioClearAsync(CancellationToken ct) => Group.SendAsync(LiveHubContract.AudioClear, ct);

    public Task CaptionAsync(CaptionDto caption, CancellationToken ct) => Group.SendAsync(LiveHubContract.Caption, caption, ct);

    public Task SnapshotAsync(DebateSnapshotDto snapshot, CancellationToken ct) => Group.SendAsync(LiveHubContract.Snapshot, snapshot, ct);

    public Task EndedAsync(MatchId matchId, CancellationToken ct) => Group.SendAsync(LiveHubContract.Ended, matchId.Value, ct);

    public Task ErrorAsync(string message, CancellationToken ct) => Group.SendAsync(LiveHubContract.Error, message, ct);
}
