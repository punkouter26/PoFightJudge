using System.Threading.Channels;
using PoFightJudge.Api.Features.Fighters;
using PoFightJudge.Api.Features.Records;
using PoFightJudge.Shared.Identifiers;

namespace PoFightJudge.Api.Features.Watch;

/// <summary>
/// Rewrites the personas of the real people in a watch, after the verdict has already gone back to the room. A 2P
/// fight gets this for free — its persona write is the last step of a pipeline that was already in the background —
/// but a watch is ruled on inside the request that ends it, and a model call per person there is time the room
/// spends staring at a spinner for something it is not waiting on.
/// </summary>
public interface IWatchPersonaIntake
{
    ValueTask SubmitAsync(string userId, MatchId matchId, CancellationToken ct);
}

/// <summary>
/// A channel drained by a background loop, the same shape the analysis pipeline uses. In-memory, so a process that
/// is recycled mid-queue loses whatever had not been written — which costs somebody one rewrite of a card that is
/// rewritten after every debate anyway, and never costs them a record.
/// </summary>
public sealed partial class WatchPersonaBacklog(IServiceScopeFactory scopes, ILogger<WatchPersonaBacklog> logger)
    : BackgroundService, IWatchPersonaIntake
{
    private readonly Channel<(string UserId, MatchId MatchId)> _queue = Channel.CreateUnbounded<(string, MatchId)>();

    public ValueTask SubmitAsync(string userId, MatchId matchId, CancellationToken ct) => _queue.Writer.WriteAsync((userId, matchId), ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var (userId, matchId) in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            await WriteAsync(userId, matchId, stoppingToken);
        }
    }

    /// <summary>One job, guarded: only cancellation may escape, or the whole drain loop stops with it.</summary>
    private async Task WriteAsync(string userId, MatchId matchId, CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var matches = scope.ServiceProvider.GetRequiredService<IMatchRepository>();

            // Read back rather than passed in: by the time this runs the match may have been deleted, and a persona
            // written from an argument somebody took back is exactly what a delete is supposed to prevent.
            if (await matches.GetAsync(userId, matchId, ct) is not { } match)
            {
                return;
            }

            // No report: a watch is judged as a match rather than person by person. Their own words carry it.
            await scope.ServiceProvider.GetRequiredService<IFighterPersonaWriter>().WriteAsync(match, report: null, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutting down. The next debate rewrites the card.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogFailed(logger, matchId.Value, ex);
        }
    }

    [LoggerMessage(EventId = 5111, Level = LogLevel.Warning, Message = "Watch {MatchId}: the personas could not be rewritten; the match and the records are unaffected")]
    private static partial void LogFailed(ILogger logger, string matchId, Exception ex);
}
