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

    /// <summary>Give up after this long rather than spinning forever in front of somebody.</summary>
    internal static readonly TimeSpan PollLimit = TimeSpan.FromMinutes(5);

    private readonly CancellationTokenSource _leaving = new();
    private AnalysisReportDto? _report;

    /// <summary>The match row, read once for the share control: the report says what was decided, not who owns it.</summary>
    private MatchDto? _match;
    private AnalysisStatus _status = AnalysisStatus.Queued;
    private string? _problem;
    private bool _retrying;
    private bool _finished;
    private int _tab;

    [Parameter] public string MatchIdText { get; set; } = string.Empty;

    [Inject] private IApiClient Api { get; set; } = default!;

    [Inject] private TimeProvider Clock { get; set; } = default!;

    [Inject] private NavigationManager Nav { get; set; } = default!;

    [Inject] private SfxInterop Sound { get; set; } = default!;

    [Inject] private ParticleInterop Particles { get; set; } = default!;

    [Inject] private GfxInterop Gfx { get; set; } = default!;

    [Inject] private IJSRuntime Js { get; set; } = default!;

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

    private string WinnerLine => _report is null
        ? string.Empty
        : $"{(_report.Overall.Overall == Speaker.Player1 ? _report.Player1.Name : _report.Player2.Name)} took it.";

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

        await PollAsync(id);
    }

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
                await Task.Delay(PollInterval, Clock, _leaving.Token);
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

        await Sound.PlayAsync(Sfx.Fanfare, ct: _leaving.Token);

        // After the render that put the cards on the page, or there is nothing to measure the burst against.
        await Task.Yield();
        var won = _report.Overall.Overall == Speaker.Player1 ? _report.Player1.Name : _report.Player2.Name;
        await Particles.BurstAsync(RevealSelector, ParticleInterop.Confetti, $".player[data-tag='{won}']", _leaving.Token);

        // T111: the seals on each fallacy slam in once the verdict card is on the page.
        try
        {
            await Js.InvokeVoidAsync("PoFallacyStamp.slam", ".fallacies");
        }
        catch (JSException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (TaskCanceledException)
        {
        }
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
        catch (ApiException ex)
        {
            _problem = ex.Summary;
        }
        catch (HttpRequestException ex)
        {
            _problem = $"It could not be sent back to be read ({ex.Message}).";
        }
        finally
        {
            _retrying = false;
        }
    }

    public void Dispose()
    {
        _leaving.Cancel();
        _leaving.Dispose();

        // This page disposes synchronously, so the canvases are let go rather than awaited. Neither call can fail
        // in a way anybody could act on.
        Gfx.Release(RevealSelector);
        Particles.Release(RevealSelector);
    }
}
