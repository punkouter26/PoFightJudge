using Microsoft.JSInterop;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Client.Services;

/// <summary>
/// The .NET side of <c>PoAudio</c>. Pages hand it base64 audio with the format it actually came back in — the server
/// chain can fall back mid-line, so decoding on the requested format would be the bug this carries the tag to avoid.
/// </summary>
public sealed class AudioInterop(IJSRuntime js) : IAsyncDisposable
{
    public const string Play = "PoAudio.play";
    public const string Enqueue = "PoAudio.enqueue";
    public const string Stop = "PoAudio.stop";
    public const string Pending = "PoAudio.pending";

    private DotNetObjectReference<AudioInterop>? _self;

    /// <summary>Raised when everything queued has finished playing.</summary>
    public event EventHandler? PlaybackEnded;

    /// <summary>Plays one utterance, replacing anything already playing. Returns its duration.</summary>
    public async Task<TimeSpan> PlayAsync(TtsAudioDto audio, CancellationToken ct = default)
    {
        if (audio.IsEmpty)
        {
            return TimeSpan.Zero;
        }

        var seconds = await js.InvokeAsync<double>(Play, ct, audio.Base64, audio.Format);
        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>Appends a chunk to the rolling schedule so a line synthesized clause by clause plays continuously.</summary>
    public async Task<TimeSpan> EnqueueAsync(TtsAudioDto audio, CancellationToken ct = default)
    {
        if (audio.IsEmpty)
        {
            return TimeSpan.Zero;
        }

        var seconds = await js.InvokeAsync<double>(Enqueue, ct, audio.Base64, audio.Format);
        return TimeSpan.FromSeconds(seconds);
    }

    public ValueTask StopAsync(CancellationToken ct = default) => js.InvokeVoidAsync(Stop, ct);

    /// <summary>Seconds of audio still scheduled ahead of now — what a page waits on before advancing a round.</summary>
    public async Task<TimeSpan> PendingAsync(CancellationToken ct = default) =>
        TimeSpan.FromSeconds(await js.InvokeAsync<double>(Pending, ct));

    /// <summary>Asks the browser to call back when the queue drains; the callback raises <see cref="PlaybackEnded"/>.</summary>
    public async Task WatchForEndAsync(CancellationToken ct = default)
    {
        _self ??= DotNetObjectReference.Create(this);
        await js.InvokeVoidAsync("PoAudio.onEnded", ct, _self);
    }

    [JSInvokable]
    public void OnPlaybackEnded() => PlaybackEnded?.Invoke(this, EventArgs.Empty);

    public async ValueTask DisposeAsync()
    {
        try
        {
            await js.InvokeVoidAsync(Stop);
        }
        catch (JSException)
        {
            // The circuit or page is already gone; nothing to stop.
        }
        catch (InvalidOperationException)
        {
            // Prerendering: no JS runtime to talk to.
        }

        _self?.Dispose();
        _self = null;
    }
}
