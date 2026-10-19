using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using PoFightJudge.Client.Services;
using PoFightJudge.Client.Stats;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Client.Pages;

/// <summary>
/// What a fight came to. The report is not ready the moment the fight ends, so the page waits for it and says what
/// it is waiting for; when it fails, it says so and offers to read it again.
/// </summary>
public sealed partial class Verdict : ComponentBase, IDisposable
{
    /// <summary>The element the confetti falls into. It covers the ruling and both fighters' cards.</summary>
    public const string RevealSelector = ".verdict";

    /// <summary>How often to ask. Long enough not to hammer the API, short enough that a finished report appears.</summary>
    internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    /// <summary>How often to ask while the server has promised to say when something changes: a backstop, not the plan.</summary>
    internal static readonly TimeSpan ListeningInterval = TimeSpan.FromSeconds(20);

    /// <summary>Give up after this long rather than spinning forever in front of somebody.</summary>
    internal static readonly TimeSpan PollLimit = TimeSpan.FromMinutes(5);

    /// <summary>How long the drumroll runs before the winner is named. Verdict.razor.css lands the headline on it.</summary>
    internal static readonly TimeSpan RevealAt = TimeSpan.FromSeconds(1.8);

    /// <summary>How fresh a ruling has to be to be revealed rather than shown: one read straight after its fight.</summary>
    private static readonly TimeSpan Fresh = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Whether a ruling generated at <paramref name="generatedAt"/> is revealed — drumroll, then the name — or simply
    /// shown. Opened from the history a day later it is a document, and a drumroll every time is a toll.
    /// </summary>
    public static bool Staged(DateTimeOffset generatedAt, DateTimeOffset now) => now - generatedAt <= Fresh;

    /// <summary>The reveal is running: the page holds its parts back until the drumroll has landed.</summary>
    private bool _staged;

    /// <summary>Cancelled by Skip (or by leaving), which ends the reveal's waits at once.</summary>
    private CancellationTokenSource? _skip;

    private readonly CancellationTokenSource _leaving = new();
    private AnalysisReportDto? _report;

    /// <summary>The match row, read once for the share control: the report says what was decided, not who owns it.</summary>
    private MatchDto? _match;
    private AnalysisStatus _status = AnalysisStatus.Queued;
    private string? _problem;
    private bool _retrying;
    private bool _finished;
    private int _tab;

    /// <summary>The line the server announces a new stage on, when it could be opened.</summary>
    private AnalysisSignal? _signal;

    /// <summary>Completed by an announcement, which ends whatever wait the polling loop is in.</summary>
    private TaskCompletionSource _nudge = new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Parameter] public string MatchIdText { get; set; } = string.Empty;

    [Inject] private IApiClient Api { get; set; } = default!;

    [Inject] private TimeProvider Clock { get; set; } = default!;

    [Inject] private NavigationManager Nav { get; set; } = default!;

    [Inject] private SfxInterop Sound { get; set; } = default!;

    [Inject] private ParticleInterop Particles { get; set; } = default!;

    [Inject] private GfxInterop Gfx { get; set; } = default!;

    [Inject] private FxInterop Fx { get; set; } = default!;

    [Inject] private IJSRuntime Js { get; set; } = default!;

    [Inject] private IServiceProvider Services { get; set; } = default!;

    private string Title => _report is null ? "Reading it back" : _report.Topic is { Length: > 0 } topic ? topic : "The ruling";

    private string Lead => _report is null
        ? "The fight is over. Somebody is going through it."
        : "What was measured, what was made of it, and who took it.";

    private string Waiting => _status switch
    {
        AnalysisStatus.Diarizing => "Working out who said what.",
        AnalysisStatus.Judging => "Reading the argument.",
        _ => "Queued.",
    };

    /// <summary>Who took it, or nobody: a fight too short to rule on has a report, and no winner.</summary>
    private string? Winner => _report is not { Overall.Ruled: true }
        ? null
        : _report.Overall.Overall == Speaker.Player1 ? _report.Player1.Name : _report.Player2.Name;

    /// <summary>Who did not take it, when somebody did.</summary>
    private string? Loser => _report is not { Overall.Ruled: true }
        ? null
        : _report.Overall.Overall == Speaker.Player1 ? _report.Player2.Name : _report.Player1.Name;

    /// <summary>Both sides in a fixed order, so every tab lays them out the same way round.</summary>
    private IEnumerable<(Speaker Side, PlayerReportDto Report)> Sides =>
        _report is null ? [] : [(Speaker.Player1, _report.Player1), (Speaker.Player2, _report.Player2)];

    private static string GroupName(StatGroup group) => group switch
    {
        StatGroup.Floor => "Holding the floor",
        StatGroup.Words => "The words",
        _ => "The manner",
    };

    protected override async Task OnInitializedAsync()
    {
        if (!MatchId.TryParse(MatchIdText, null, out var id))
        {
            _problem = "That is not a fight.";
            return;
        }

        // Alongside the report rather than before it: a share control that is not there yet must never hold up the
        // ruling, and a match that cannot be read just means the control does not appear.
        try
        {
            _match = await Api.GetMatchAsync(id, _leaving.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (HttpRequestException)
        {
            _match = null;
        }

        // Told when the reading moves on, where the line can be opened. Where it cannot, the page asks on a timer.
        if (Services.GetService<LiveConnectionFactory>() is { } connections)
        {
            _signal = connections.CreateSignal();
            await _signal.ListenAsync(id, Nudge, _leaving.Token);
        }

        await PollAsync(id);
    }

    /// <summary>Something changed: stop waiting and ask now.</summary>
    private void Nudge() => Interlocked.Exchange(ref _nudge, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();

    /// <summary>
    /// Once the report is on the page, it is finished like a piece of film: a vignette that pulls the eye into the
    /// ruling, and grain over the top. Over the page rather than behind it — grain behind the cards is a texture.
    /// </summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_report is not null && !_finished)
        {
            _finished = true;
            await Gfx.MountAsync(RevealSelector, Shaders.Grain, level: null, over: true, _leaving.Token);
        }
    }

    /// <summary>Keeps the page's own copy of the match true after the share is made or taken back.</summary>
    private void OnShareChanged(string? token)
    {
        if (_match is not null)
        {
            _match = _match with { ShareToken = token };
        }
    }

    /// <summary>Asks until there is something to show, or until it is clear nothing is coming.</summary>
    private async Task PollAsync(MatchId id)
    {
        var started = Clock.GetUtcNow();
        while (!_leaving.IsCancellationRequested)
        {
            try
            {
                var answer = await Api.GetAnalysisAsync(id, _leaving.Token);
                _status = answer.Status;

                if (answer.Report is not null)
                {
                    _report = answer.Report;
                    _problem = null;
                    _staged = Staged(_report.GeneratedAt, Clock.GetUtcNow());
                    StateHasChanged();
                    await CelebrateAsync();
                    return;
                }

                if (answer.Status == AnalysisStatus.Failed)
                {
                    _problem = answer.Error ?? "It could not be read.";
                    StateHasChanged();
                    return;
                }
            }
            catch (ApiException ex)
            {
                _problem = ex.Status == 404 ? "That fight is not here." : ex.Summary;
                StateHasChanged();
                return;
            }
            catch (HttpRequestException)
            {
                // A dropped request is not a failed analysis; the next ask will find out either way.
            }

            if (Clock.GetUtcNow() - started > PollLimit)
            {
                _problem = "This is taking longer than it should. Try reading it again.";
                StateHasChanged();
                return;
            }

            StateHasChanged();
            try
            {
                // With the line up the timer is only a backstop, so it is a slow one; a nudge ends the wait at once.
                var wait = Task.Delay(_signal is { IsListening: true } ? ListeningInterval : PollInterval, Clock, _leaving.Token);
                await Task.WhenAny(wait, Volatile.Read(ref _nudge).Task);
                _leaving.Token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// The moment the report lands. Somebody has been watching a spinner for a minute: the ruling arriving is the
    /// one thing on this page worth announcing, and the confetti falls on the card of whoever took it.
    /// </summary>
    private async Task CelebrateAsync()
    {
        if (_report is null)
        {
            return;
        }

        // A fresh ruling gets its drumroll, and the name lands on the beat — or at once, for somebody who skipped.
        _skip = CancellationTokenSource.CreateLinkedTokenSource(_leaving.Token);
        if (_staged)
        {
            await Sound.PlayAsync(Sfx.Drumroll, ct: _leaving.Token);
            if (!await HoldAsync(RevealAt))
            {
                return;
            }

            await GavelAsync();
        }

        // Nobody won a fight that was too short to rule on, and there is nothing to celebrate about that.
        if (Winner is { } won)
        {
            await Sound.PlayAsync(Sfx.Fanfare, ct: _leaving.Token);

            // After the render that put the cards on the page, or there is nothing to measure the burst against.
            await Task.Yield();
            await Particles.BurstAsync(RevealSelector, ParticleInterop.Confetti, $".player[data-tag='{won}']", _leaving.Token);
        }

        // The loser's card has risen by now, and it breaks: the crack in its CSS, the shards thrown off it here.
        if (_staged && await HoldAsync(ShatterAfter) && Loser is { } lost)
        {
            await Sound.PlayAsync(Sfx.Shatter, ct: _leaving.Token);
            await Particles.BurstAsync(RevealSelector, ParticleInterop.Shatter, $".player[data-tag='{lost}']", _leaving.Token);
        }

        // Once the rest has risen there is nothing left to skip: the button goes and the page is a page again.
        if (_staged && await HoldAsync(Settle))
        {
            _staged = false;
            StateHasChanged();
        }
    }

    /// <summary>Waits <paramref name="span"/>, or less if Skip is pressed. False when the page is being left.</summary>
    private async Task<bool> HoldAsync(TimeSpan span)
    {
        try
        {
            await Task.Delay(span, Clock, _skip!.Token);
        }
        catch (OperationCanceledException)
        {
            // Skipped, or leaving; which one is answered below.
        }

        return !_leaving.IsCancellationRequested;
    }

    /// <summary>From the name landing to the loser's card breaking, once it has risen (Verdict.razor.css cracks it then).</summary>
    private static readonly TimeSpan ShatterAfter = TimeSpan.FromSeconds(1.5);

    /// <summary>From the card breaking to the last part of the report having risen.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(0.6);

    /// <summary>The PoFx class the report jolts with on the beat (Verdict.razor.css).</summary>
    private const string SlamEffect = "slam";

    /// <summary>
    /// The beat: the gavel comes down in a courtroom, a ring goes out through the grain from the winner's name, and the
    /// whole report takes the blow.
    /// </summary>
    private async Task GavelAsync()
    {
        await Sound.PlayAsync(Sfx.Slam, ct: _leaving.Token);
        await Gfx.FocusAsync(RevealSelector, ".ruling h2", _leaving.Token);
        await Gfx.ShockAsync(RevealSelector, _leaving.Token);
        await Fx.BurstAsync(SlamEffect, RevealSelector, _leaving.Token);
    }

    /// <summary>Straight to the ruling: every part shows now, and the drumroll's wait ends.</summary>
    private void Skip()
    {
        _staged = false;
        _skip?.Cancel();
    }

    private async Task RetryAsync()
    {
        if (_retrying || !MatchId.TryParse(MatchIdText, null, out var id))
        {
            return;
        }

        _retrying = true;
        _problem = null;
        _status = AnalysisStatus.Queued;
        StateHasChanged();

        try
        {
            await Api.RetryAnalysisAsync(id, _leaving.Token);
            await PollAsync(id);
        }
        catch (Exception ex) when (ApiFailure.Describe(ex, "It could not be sent back to be read") is { } why)
        {
            _problem = why;
        }
        finally
        {
            _retrying = false;
        }
    }

    public void Dispose()
    {
        _leaving.Cancel();
        _skip?.Dispose();
        _leaving.Dispose();

        // This page disposes synchronously, so the canvases are let go rather than awaited. Neither call can fail
        // in a way anybody could act on.
        Gfx.Release(RevealSelector);
        Particles.Release(RevealSelector);

        // Fire and forget, for the same reason: closing a socket on the way out has nothing to report.
        _ = _signal?.DisposeAsync().AsTask();
    }
}
