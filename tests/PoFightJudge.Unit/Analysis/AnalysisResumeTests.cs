using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using PoFightJudge.Api.Features.Analysis;
using PoFightJudge.Api.Features.Fight;
using PoFightJudge.Api.Features.Records;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Unit.Analysis;

/// <summary>
/// Picking a fight's analysis back up after the host went away underneath it.
/// </summary>
/// <remarks>
/// The queue is an in-memory Channel drained by a BackgroundService, and the F1 plan has no Always On: the site
/// unloads when nobody has asked for it in twenty minutes. Everything queued and everything in flight died with
/// it, and the match row was left saying Analyzing — a spinner with nothing behind it, for good, with no way back
/// except the manual retry endpoint nobody knew to press.
///
/// Anything still marked Analyzing when the host comes back is either mid-flight from the process that died or was
/// never started. Both want the same thing. What does not is a fight old enough that nobody is waiting on it any
/// more, whose recording may not even be in storage: that is marked failed and told so, rather than quietly
/// re-run.
/// </remarks>
public class AnalysisResumeTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    private static MatchDto Match(SessionStatus status, DateTimeOffset startedAt, string user = "u1") => new(
        MatchId.New(),
        user,
        MatchMode.Fight,
        startedAt,
        startedAt.AddMinutes(3),
        "the thermostat",
        MatchSide.Human("AL", "Al"),
        MatchSide.Human("SM", "Sam"),
        SessionPhase.Verdict,
        status,
        Winner: string.Empty,
        Verdict: null,
        IsFake: false)
    {
        Persona = "referee",
        AudioBlobName = "m/players.opus",
    };

    private static (AnalysisResumer Resumer, IMatchRepository Matches, List<(string User, MatchId Id)> Queued) Build(
        IReadOnlyList<MatchDto> unfinished,
        int maxAgeHours = 24)
    {
        var matches = Substitute.For<IMatchRepository>();
        matches.ListUnfinishedAnalysesAsync(Arg.Any<CancellationToken>()).Returns(unfinished);

        var intake = new RecordingIntake();

        var clock = new FakeTimeProvider(Now);
        var options = Options.Create(new AnalysisOptions { ResumeMaxAgeHours = maxAgeHours });
        return (new AnalysisResumer(matches, intake, clock, options, NullLogger<AnalysisResumer>.Instance), matches, intake.Submitted);
    }

    [Fact]
    public async Task A_fight_the_host_died_halfway_through_is_picked_back_up()
    {
        var stranded = Match(SessionStatus.Analyzing, Now.AddMinutes(-5));
        var (resumer, _, queued) = Build([stranded]);

        await resumer.ResumeAsync(CancellationToken.None);

        queued.Select(q => q.Id).Should().Equal([stranded.Id], "it was left mid-flight, and nothing else was ever going to finish it");
    }

    [Fact]
    public async Task A_fight_nobody_is_waiting_on_any_more_is_told_so_rather_than_re_run()
    {
        var ancient = Match(SessionStatus.Analyzing, Now.AddDays(-3));
        var (resumer, matches, queued) = Build([ancient]);

        await resumer.ResumeAsync(CancellationToken.None);

        queued.Should().BeEmpty("three days on, the recording may not even be there");
        await matches.Received(1).SaveAnalysisAsync(
            Arg.Is<AnalysisRecordDto>(a => a.MatchId == ancient.Id && a.Status == AnalysisStatus.Failed),
            Arg.Any<CancellationToken>());
        await matches.Received(1).UpsertAsync(
            Arg.Is<MatchDto>(m => m.Id == ancient.Id && m.Status == SessionStatus.Failed),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Each_fight_goes_back_under_the_account_that_argued_it()
    {
        var mine = Match(SessionStatus.Analyzing, Now.AddMinutes(-1), user: "u1");
        var theirs = Match(SessionStatus.Analyzing, Now.AddMinutes(-2), user: "u2");
        var matches = Substitute.For<IMatchRepository>();
        matches.ListUnfinishedAnalysesAsync(Arg.Any<CancellationToken>()).Returns([mine, theirs]);

        var intake = new RecordingIntake();

        var resumer = new AnalysisResumer(
            matches, intake, new FakeTimeProvider(Now),
            Options.Create(new AnalysisOptions()), NullLogger<AnalysisResumer>.Instance);
        await resumer.ResumeAsync(CancellationToken.None);

        intake.Submitted.Should().BeEquivalentTo([("u1", mine.Id), ("u2", theirs.Id)]);
    }

    /// <summary>A fight with no recording cannot be read however young it is; re-queueing it only fails again.</summary>
    [Fact]
    public async Task A_fight_with_no_recording_is_failed_rather_than_queued()
    {
        var soundless = Match(SessionStatus.Analyzing, Now.AddMinutes(-5)) with { AudioBlobName = null };
        var (resumer, matches, queued) = Build([soundless]);

        await resumer.ResumeAsync(CancellationToken.None);

        queued.Should().BeEmpty();
        await matches.Received(1).SaveAnalysisAsync(
            Arg.Is<AnalysisRecordDto>(a => a.Status == AnalysisStatus.Failed), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Nothing_stranded_means_nothing_happens()
    {
        var (resumer, matches, queued) = Build([]);

        await resumer.ResumeAsync(CancellationToken.None);

        queued.Should().BeEmpty();
        await matches.DidNotReceive().UpsertAsync(Arg.Any<MatchDto>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Startup is not a place to throw: a storage hiccup here must not stop the app coming up.</summary>
    [Fact]
    public async Task A_storage_failure_while_looking_is_swallowed()
    {
        var matches = Substitute.For<IMatchRepository>();
        matches.ListUnfinishedAnalysesAsync(Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<MatchDto>>>(_ => throw new InvalidOperationException("storage is down"));

        var resumer = new AnalysisResumer(
            matches, new RecordingIntake(), new FakeTimeProvider(Now),
            Options.Create(new AnalysisOptions()), NullLogger<AnalysisResumer>.Instance);

        var resume = async () => await resumer.ResumeAsync(CancellationToken.None);

        await resume.Should().NotThrowAsync();
    }

    /// <summary>
    /// Hand-written rather than substituted: SubmitAsync returns a ValueTask, and a callback that builds one to
    /// hand back is exactly the single-consumption bug CA2012 exists to catch.
    /// </summary>
    private sealed class RecordingIntake : IAnalysisIntake
    {
        public List<(string User, MatchId Id)> Submitted { get; } = [];

        public ValueTask SubmitAsync(string userId, MatchId matchId, CancellationToken ct)
        {
            Submitted.Add((userId, matchId));
            return ValueTask.CompletedTask;
        }
    }
}
