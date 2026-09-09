using Microsoft.JSInterop;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Client.Services;

/// <summary>
/// The .NET side of <c>PoListen</c>: the browser's own recogniser, listening alongside a live fight and collecting
/// what it heard into <see cref="HeardTranscript"/>.
/// </summary>
/// <remarks>
/// Best-effort throughout. A browser without the API, a refused permission or a session that dies mid-fight all
/// mean the analysis reads what it read before — the host's live captions, or a paid diarization. Nothing here is
/// allowed to interrupt a fight to report that it is not working.
/// </remarks>
public sealed class ListenInterop(IJSRuntime js) : IAsyncDisposable
{
    public const string Available = "PoListen.available";
    public const string Start = "PoListen.start";
    public const string Stop = "PoListen.stop";

    private DotNetObjectReference<ListenInterop>? _self;
    private HeardTranscript? _heard;

    /// <summary>Whether this browser has a recogniser at all.</summary>
    public async Task<bool> AvailableAsync(CancellationToken ct = default)
    {
        try
        {
            return await js.InvokeAsync<bool>(Available, ct);
        }
        catch (JSException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            // Prerendering: there is no browser to ask yet.
            return false;
        }
    }

    /// <summary>Starts listening. False when this browser will not, which is not a problem worth showing anybody.</summary>
    public async Task<bool> StartAsync(CancellationToken ct = default)
    {
        try
        {
            _heard = new HeardTranscript();
            _self ??= DotNetObjectReference.Create(this);
            return await js.InvokeAsync<bool>(Start, ct, _self);
        }
        catch (JSException)
        {
            _heard = null;
            return false;
        }
        catch (InvalidOperationException)
        {
            _heard = null;
            return false;
        }
    }

    /// <summary>Stops listening and hands back everything heard, or null when that was nothing.</summary>
    public async Task<TranscriptDto?> StopAsync(CancellationToken ct = default)
    {
        try
        {
            await js.InvokeVoidAsync(Stop, ct);
        }
        catch (JSException)
        {
            // The session is gone; what was already heard is still worth posting.
        }
        catch (InvalidOperationException)
        {
        }

        var transcript = _heard?.ToTranscript();
        _heard = null;
        return transcript;
    }

    /// <summary>One settled utterance, with the window of the fight it was heard in.</summary>
    [JSInvokable]
    public void OnHeard(string text, double startSeconds, double endSeconds) =>
        _heard?.Heard(text, startSeconds, endSeconds);

    public async ValueTask DisposeAsync()
    {
        try
        {
            await js.InvokeVoidAsync(Stop);
        }
        catch (JSException)
        {
        }
        catch (InvalidOperationException)
        {
        }

        _self?.Dispose();
        _self = null;
    }
}
