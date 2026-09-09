using PoFightJudge.Client.Services;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Client.Pages;

/// <summary>
/// The person's spoken turn. The microphone opens by itself when the turn arrives and closes when they stop
/// talking; what it heard is transcribed into the box for them to read before it counts, because a transcriber
/// that mishears would otherwise put words on their record.
/// </summary>
/// <remarks>
/// There is still a button to stop early, and still a box to type in. The browser's own recogniser stays on offer
/// where it exists: it is instant and free, and some people would rather watch words appear than talk to a meter.
/// </remarks>
public sealed partial class WatchPlay
{
    /// <summary>How often the level is read. Ten times a second is smooth to look at and cheap to poll.</summary>
    private static readonly TimeSpan LevelInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>Quiet enough to count as not talking. Set above room noise, below anything said out loud.</summary>
    private const double SilenceLevel = 0.06;

    /// <summary>
    /// How long a pause has to run before the turn is taken as finished. Long enough to think mid-sentence, short
    /// enough that nobody wonders whether the thing is still listening.
    /// </summary>
    private static readonly TimeSpan SilenceToEnd = TimeSpan.FromSeconds(2.5);

    /// <summary>Nobody is cut off before they have started: silence only ends a turn once something was said.</summary>
    private bool _heardSomething;

    private bool _recording;
    private bool _transcribing;
    private bool _heardNothing;
    private string? _micProblem;
    private CancellationTokenSource? _listening;

    /// <summary>Recording is offered only when the server has a transcriber to finish the turn.</summary>
    private bool CanRecord => Features.Flags.HumanInWatch;

    /// <summary>The browser's own recogniser, when this browser has one and the server has not turned it off.</summary>
    private bool CanDictate => Features.Flags.BrowserSpeechRecognition;

    /// <summary>Opens the microphone for a turn that has just begun. A failure here leaves the box to type in.</summary>
    private async Task BeginListeningAsync()
    {
        if (!CanRecord || _recording || _transcribing)
        {
            return;
        }

        await StartRecordingAsync();
        if (_recording)
        {
            StartLevelWatch();
        }

        StateHasChanged();
    }

    /// <summary>
    /// Watches the level while they speak: it drives the meter, and a long enough pause after they have actually
    /// said something ends the turn without them having to reach for anything.
    /// </summary>
    private void StartLevelWatch()
    {
        _listening?.Dispose();
        var watching = CancellationTokenSource.CreateLinkedTokenSource(_leaving.Token);
        _listening = watching;
        _heardSomething = false;
        var quietSince = Clock.GetUtcNow();

        _ = InvokeAsync(async () =>
        {
            try
            {
                while (!watching.Token.IsCancellationRequested && _recording)
                {
                    await Task.Delay(LevelInterval, Clock, watching.Token);

                    // Only the decision is made here; the meter is painted by the browser from the level the
                    // recorder publishes, so this loop never re-renders anything.
                    var level = await Mic.LevelAsync(watching.Token);
                    var now = Clock.GetUtcNow();
                    if (level >= SilenceLevel)
                    {
                        _heardSomething = true;
                        quietSince = now;
                    }
                    else if (_heardSomething && now - quietSince >= SilenceToEnd)
                    {
                        await StopRecordingAsync();
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // The turn ended, or the page is going away.
            }
        });
    }

    private void StopLevelWatch()
    {
        _listening?.Cancel();
        _listening?.Dispose();
        _listening = null;
    }

    /// <summary>
    /// Lets the microphone go without transcribing what it heard. Somebody who has started typing has chosen how
    /// they are answering: the pause that ends a spoken turn will never come, and a clip they did not speak into
    /// must not be written over the top of what they wrote.
    /// </summary>
    private async Task StopListeningAsync()
    {
        if (!_recording)
        {
            return;
        }

        _recording = false;
        StopLevelWatch();
        await Mic.CancelAsync(_leaving.Token);
    }

    private async Task StartRecordingAsync()
    {
        _micProblem = null;
        _heardNothing = false;

        var error = await Mic.StartAsync(ct: _leaving.Token);
        if (error is not null)
        {
            _micProblem = MicMessage(error);
            return;
        }

        _recording = true;
    }

    private async Task StopRecordingAsync()
    {
        if (!_recording)
        {
            return;
        }

        _recording = false;
        StopLevelWatch();
        _transcribing = true;
        StateHasChanged();

        try
        {
            var wav = await Mic.StopAsync(_leaving.Token);
            if (wav.Length == 0)
            {
                _micProblem = "Nothing came through the microphone. Try that again.";
                return;
            }

            var heard = await Api.TranscribeAsync(new TranscribeRequest(wav), _leaving.Token);
            _isFake |= heard.IsFake;

            var said = heard.Text.Trim();
            if (said.Length == 0)
            {
                // A legitimate outcome, not an error: the transcriber heard the clip and there were no words in it.
                _heardNothing = true;
                return;
            }

            _line = _line.Trim().Length == 0 ? said : $"{_line.Trim()} {said}";
        }
        catch (ApiException ex)
        {
            _micProblem = ex.Summary;
        }
        catch (HttpRequestException ex)
        {
            _micProblem = $"The clip could not be sent ({ex.Message}).";
        }
        finally
        {
            _transcribing = false;
            StateHasChanged();
        }
    }

    /// <summary>Says it again: throws away what is in the box and listens from the start.</summary>
    private async Task ListenAgainAsync()
    {
        _line = string.Empty;
        _heardNothing = false;
        await BeginListeningAsync();
    }

    /// <summary>What the browser's recogniser heard. It arrives a phrase at a time, so it is appended, not replaced.</summary>
    private void OnDictated(string? heard)
    {
        var said = heard?.Trim() ?? string.Empty;
        if (said.Length == 0)
        {
            return;
        }

        _heardNothing = false;
        _line = _line.Trim().Length == 0 ? said : $"{_line.Trim()} {said}";
    }

    /// <summary>Turns a browser error name into advice a person can act on.</summary>
    private static string MicMessage(string error) => error switch
    {
        "NotAllowedError" or "SecurityError" => "The microphone was blocked. Allow it for this site and try again.",
        "NotFoundError" or "DevicesNotFoundError" => "No microphone was found. Type your line instead.",
        "NotReadableError" or "TrackStartError" => "The microphone is in use by something else.",
        "NotSupportedError" => "This browser will not record. Type your line instead.",
        _ => $"The microphone could not be started ({error}).",
    };
}
