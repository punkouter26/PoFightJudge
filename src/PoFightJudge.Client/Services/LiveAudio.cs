using Microsoft.JSInterop;

namespace PoFightJudge.Client.Services;

/// <summary>Receives one 100 ms frame of 16 kHz PCM from the capture worklet.</summary>
public interface IAudioFrameSink
{
    Task OnAudioFrameAsync(byte[] pcm16k);
}

/// <summary>
/// The audio of a live fight: the microphone going up and the host's voice coming back. Separate from
/// <see cref="AudioInterop"/>, which plays whole synthesized lines for a watch — different contracts, and only one
/// of them is ever on screen at a time.
/// </summary>
public interface ILiveAudio : IAsyncDisposable
{
    /// <summary>
    /// Asks for the microphone and starts streaming frames to <paramref name="sink"/>. Returns the device's sample
    /// rate, or an error name when the microphone was refused, so the page can say which problem it is.
    /// </summary>
    Task<CaptureStart> StartCaptureAsync(IAudioFrameSink sink, CancellationToken ct = default);

    /// <summary>Queues a chunk of the host's voice, 24 kHz PCM, to play seamlessly after whatever is already queued.</summary>
    Task PlayAsync(byte[] pcm24k, CancellationToken ct = default);

    /// <summary>Drops what is queued: the host was cut off and must not carry on talking over the interruption.</summary>
    Task ClearPlaybackAsync(CancellationToken ct = default);

    Task StopAsync(CancellationToken ct = default);
}

/// <summary>What came back from asking for the microphone.</summary>
public sealed record CaptureStart(int SampleRate, string Error)
{
    public bool Started => Error.Length == 0;
}

/// <summary>
/// Bridges the worklet's callback into .NET. Passed to the browser as a DotNetObjectReference, so it has to be a
/// class of its own rather than a lambda.
/// </summary>
public sealed class AudioCaptureBridge(IAudioFrameSink sink)
{
    /// <summary>Named for the browser in the attribute, so the C# name can carry the suffix the analyzers want.</summary>
    [JSInvokable("OnAudioFrame")]
    public Task OnAudioFrameAsync(byte[] pcm) => sink.OnAudioFrameAsync(pcm);
}

/// <summary>The .NET side of <c>PoLive</c>.</summary>
public sealed class LiveAudio(IJSRuntime js) : ILiveAudio
{
    public const string StartCapture = "PoLive.startCapture";
    public const string Play = "PoLive.play";
    public const string Clear = "PoLive.clear";
    public const string Stop = "PoLive.stop";

    private DotNetObjectReference<AudioCaptureBridge>? _bridge;

    public async Task<CaptureStart> StartCaptureAsync(IAudioFrameSink sink, CancellationToken ct = default)
    {
        _bridge?.Dispose();
        _bridge = DotNetObjectReference.Create(new AudioCaptureBridge(sink));

        try
        {
            return await js.InvokeAsync<CaptureStart>(StartCapture, ct, _bridge) ?? new CaptureStart(0, "NotSupportedError");
        }
        catch (JSException ex)
        {
            return new CaptureStart(0, ex.Message);
        }
    }

    public Task PlayAsync(byte[] pcm24k, CancellationToken ct = default) => js.InvokeVoidAsync(Play, ct, pcm24k).AsTask();

    public Task ClearPlaybackAsync(CancellationToken ct = default) => js.InvokeVoidAsync(Clear, ct).AsTask();

    public async Task StopAsync(CancellationToken ct = default)
    {
        await js.InvokeVoidAsync(Stop, ct);
        _bridge?.Dispose();
        _bridge = null;
    }

    /// <summary>Leaving the page releases the microphone; a recording light left on is not something to explain away.</summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync();
        }
        catch (JSException)
        {
            // The page is already going away.
        }
        catch (JSDisconnectedException)
        {
        }
        catch (InvalidOperationException)
        {
            // Prerendering: there was never a browser holding a microphone.
        }

        _bridge?.Dispose();
        _bridge = null;
    }
}
