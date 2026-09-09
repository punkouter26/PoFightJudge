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
    public const string Devices = "PoMic.devices";

    /// <summary>
    /// Starts recording. Returns null when the microphone is live, or the browser's error name when it is not —
    /// a refused permission and a missing device are different problems and need different advice.
    /// </summary>
    public async Task<string?> StartAsync(string? deviceId = null, CancellationToken ct = default)
    {
        try
        {
            var error = await js.InvokeAsync<string>(Start, ct, deviceId);
            return string.IsNullOrEmpty(error) ? null : error;
        }
        catch (JSException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Stops recording and returns the clip. An empty clip carries the reason it is empty: a microphone that heard
    /// nothing and a browser that could not read the recording back are different problems, and telling somebody who
    /// watched the meter move that nothing came through is the wrong one.
    /// </summary>
    public async Task<MicClip> StopAsync(CancellationToken ct = default)
    {
        try
        {
            return await js.InvokeAsync<MicClip>(Stop, ct) ?? MicClip.Nothing("NoAnswer");
        }
        catch (JSException ex)
        {
            return MicClip.Nothing(ex.Message);
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

    /// <summary>
    /// The microphones this browser will admit to. Labels are blank until permission has been given once, so the
    /// setup screen asks for the microphone before it offers the list — an unlabelled list of ids helps nobody.
    /// </summary>
    public async Task<IReadOnlyList<MicDevice>> DevicesAsync(CancellationToken ct = default)
    {
        try
        {
            return await js.InvokeAsync<MicDevice[]>(Devices, ct) ?? [];
        }
        catch (JSException)
        {
            return [];
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

/// <summary>
/// One recorded turn. <paramref name="Wav"/> is base64 WAV when there is a clip; when there is not,
/// <paramref name="Error"/> says which step gave up — <c>NothingCaptured</c>, or <c>DecodeFailed:{name}</c> and its
/// kin, where the name is whatever the browser called it.
/// </summary>
public sealed record MicClip(string Wav, string Error)
{
    public static MicClip Nothing(string error) => new(string.Empty, error);

    public bool IsEmpty => string.IsNullOrEmpty(Wav);
}

/// <summary>One microphone the browser is willing to name.</summary>
public sealed record MicDevice(string Id, string Label);
