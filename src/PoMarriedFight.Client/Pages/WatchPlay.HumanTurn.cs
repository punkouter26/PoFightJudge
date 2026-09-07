using PoMarriedFight.Client.Services;
using PoMarriedFight.Shared.Models;

namespace PoMarriedFight.Client.Pages;

/// <summary>
/// The person's spoken turn. There are two ways to say a line and they answer different constraints: the browser's
/// own recogniser is instant and free but only some browsers have it, while recording the clip and sending it to the
/// server works everywhere a microphone does and costs a round trip.
/// </summary>
/// <remarks>
/// Neither one sends the line. What comes back lands in the box for the person to read and fix before they say it,
/// because a transcriber that mishears is a transcriber that would otherwise put words on their record.
/// </remarks>
public sealed partial class WatchPlay
{
    private bool _recording;
    private bool _transcribing;
    private bool _heardNothing;
    private string? _micProblem;

    /// <summary>Recording is offered only when the server has a transcriber to finish the turn.</summary>
    private bool CanRecord => _flags?.HumanInWatch ?? false;

    /// <summary>The browser's own recogniser, when this browser has one and the server has not turned it off.</summary>
    private bool CanDictate => _flags?.BrowserSpeechRecognition ?? false;

    private async Task ToggleRecordingAsync()
    {
        if (_recording)
        {
            await StopRecordingAsync();
        }
        else
        {
            await StartRecordingAsync();
        }
    }

    private async Task StartRecordingAsync()
    {
        _micProblem = null;
        _heardNothing = false;

        var error = await Mic.StartAsync(_leaving.Token);
        if (error is not null)
        {
            _micProblem = MicMessage(error);
            return;
        }

        _recording = true;
    }

    private async Task StopRecordingAsync()
    {
        _recording = false;
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
