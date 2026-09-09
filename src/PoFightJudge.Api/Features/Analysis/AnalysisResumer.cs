using Microsoft.Extensions.Options;
using PoFightJudge.Api.Features.Fight;
using PoFightJudge.Api.Features.Records;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Api.Features.Analysis;

/// <summary>
/// Picks up fights whose analysis the last process did not finish.
/// </summary>
/// <remarks>
/// The queue is an in-memory <c>Channel</c> drained by a <c>BackgroundService</c>, and the F1 plan has no Always On:
/// the site unloads once nobody has asked for it in twenty minutes. Everything queued and everything in flight went
/// with it, and the row was left reading Analyzing — a spinner with nothing behind it, permanently, with no way back
/// except a retry endpoint nobody knew to press.
///
/// Anything still marked Analyzing when the host comes back was either mid-flight or never started, and both want
/// the same thing. A fight old enough that nobody is waiting on it is different: its recording may not even be in
/// storage any more, so it is failed and says so rather than being quietly re-run. A fight that fails on the way
/// through is marked Failed by the pipeline, so nothing here can loop on it.
/// </remarks>
public sealed partial class AnalysisResumer(
    IMatchRepository matches,
    IAnalysisIntake intake,
    TimeProvider clock,
    IOptions<AnalysisOptions> options,
    ILogger<AnalysisResumer> logger)
{
    public const string StaleFailure = "This fight was still being read when the app restarted, and it has been too long to pick it back up. Start it again to have another go.";

    public async Task ResumeAsync(CancellationToken ct)
    {
        try
        {
            var stranded = await matches.ListUnfinishedAnalysesAsync(ct);
            if (stranded.Count == 0)
            {
                return;
            }

            var cutoff = clock.GetUtcNow().AddHours(-Math.Max(0, options.Value.ResumeMaxAgeHours));
            var resumed = 0;
            var abandoned = 0;

            foreach (var match in stranded)
            {
                var ended = match.EndedAt ?? match.StartedAt;

                // No recording is not a matter of age: there is nothing to read however recently it happened.
                if (ended < cutoff || string.IsNullOrEmpty(match.AudioBlobName))
                {
                    await AbandonAsync(match, ct);
                    abandoned++;
                    continue;
                }

                await intake.SubmitAsync(match.UserId, match.Id, ct);
                resumed++;
            }

            LogResumed(logger, resumed, abandoned);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Startup is not a place to throw. A storage hiccup here costs the stranded fights another restart,
            // not the app coming up.
            LogResumeFailed(logger, ex);
        }
    }

    private async Task AbandonAsync(MatchDto match, CancellationToken ct)
    {
        await matches.SaveAnalysisAsync(
            new AnalysisRecordDto(match.Id, AnalysisStatus.Failed, null, StaleFailure, clock.GetUtcNow()),
            ct);
        await matches.UpsertAsync(match with { Status = SessionStatus.Failed }, ct);
    }

    [LoggerMessage(EventId = 5401, Level = LogLevel.Information, Message = "Analyses left unfinished by the last run: {Resumed} queued again, {Abandoned} too old to pick up.")]
    private static partial void LogResumed(ILogger logger, int resumed, int abandoned);

    [LoggerMessage(EventId = 5402, Level = LogLevel.Warning, Message = "Unfinished analyses could not be looked up; they keep until the next restart.")]
    private static partial void LogResumeFailed(ILogger logger, Exception ex);
}

/// <summary>
/// Runs <see cref="AnalysisResumer"/> once, after the host is up. Its own service so that a slow or failing storage
/// account delays nothing else: the pipeline is already draining by the time this adds anything to it.
/// </summary>
public sealed class AnalysisResumeService(IServiceScopeFactory scopes) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = scopes.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AnalysisResumer>().ResumeAsync(stoppingToken);
    }
}
