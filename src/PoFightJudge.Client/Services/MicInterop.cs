using Microsoft.JSInterop;

namespace PoFightJudge.Client.Services;

/// <summary>
/// The .NET side of <c>PoMic</c>: one spoken turn, recorded and handed back as base64 WAV. It records rather than
/// streams, because a WATCH turn is a whole line — the person says their piece and then the transcriber hears it.
/// </summary>
public sealed class MicInterop(IJSRuntime js) : IAsyncDisposable
{
    public const string Start = "PoMic.start";
    public const string Stop = "PoMic.stop";
    public const string Cancel = "PoMic.cancel";
    public const string Level = "PoMic.level";

    /// <summary>
    /// Starts recording. Returns null when the microphone is live, or the browser's error name when it is not —
    /// a refused permission and a missing device are different problems and need different advice.
    /// </summary>
    public async Task<string?> StartAsync(CancellationToken ct = default)
    {
        try
        {
            var error = await js.InvokeAsync<string>(Start, ct);
            return string.IsNullOrEmpty(error) ? null : error;
        }
        catch (JSException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Stops recording and returns the clip as base64 WAV; empty when nothing was captured.</summary>
    public async Task<string> StopAsync(CancellationToken ct = default)
    {
        try
        {
            return await js.InvokeAsync<string>(Stop, ct) ?? string.Empty;
        }
        catch (JSException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// How loud the microphone is right now, 0 to 1. It is read off the live stream rather than the recording, so
    /// somebody can see that they are being heard before they have said anything worth keeping.
    /// </summary>
    public async Task<double> LevelAsync(CancellationToken ct = default)
    {
        try
        {
            return await js.InvokeAsync<double>(Level, ct);
        }
        catch (JSException)
        {
            return 0;
        }
    }

    /// <summary>Throws the recording away and releases the microphone.</summary>
    public async ValueTask CancelAsync(CancellationToken ct = default)
    {
        try
        {
            await js.InvokeVoidAsync(Cancel, ct);
        }
        catch (JSException)
        {
            // Nothing was recording, or the page is going away: either way the microphone is not ours to hold.
        }
    }

    /// <summary>Leaving the page must drop the microphone; a recording light left on is not something to explain away.</summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await js.InvokeVoidAsync(Cancel);
        }
        catch (JSException)
        {
        }
        catch (InvalidOperationException)
        {
            // Prerendering: there was never a browser holding a microphone.
        }
    }
}
