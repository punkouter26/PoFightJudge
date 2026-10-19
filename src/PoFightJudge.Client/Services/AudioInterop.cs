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

    /// <summary>The middle of the stage: anybody who is not one of the two sides.</summary>
    public const double Centre = 0;

    /// <summary>
    /// Where a side is heard from: the husband's card is on the left of the stage and the wife's on the right, so that
    /// is where their voices stand. Effects that belong to a side (a slap landing on them) are placed the same way.
    /// </summary>
    public static double PanFor(bool husband) => husband ? -1 : 1;

    /// <summary>Plays one utterance, replacing anything already playing. Returns its duration.</summary>
    public Task<TimeSpan> PlayAsync(TtsAudioDto audio, CancellationToken ct = default) => PlayAsync(audio, Centre, ct);

    /// <summary>The same, heard from one side of the stage (see <see cref="PanFor"/>).</summary>
    public async Task<TimeSpan> PlayAsync(TtsAudioDto audio, double pan, CancellationToken ct = default)
    {
        if (audio.IsEmpty)
        {
            return TimeSpan.Zero;
        }

        var seconds = await js.InvokeAsync<double>(Play, ct, audio.Base64, audio.Format, pan);
        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>Appends a chunk to the rolling schedule so a line synthesized clause by clause plays continuously.</summary>
    public Task<TimeSpan> EnqueueAsync(TtsAudioDto audio, CancellationToken ct = default) => EnqueueAsync(audio, Centre, ct);

    /// <summary>The same, heard from one side of the stage (see <see cref="PanFor"/>).</summary>
    public async Task<TimeSpan> EnqueueAsync(TtsAudioDto audio, double pan, CancellationToken ct = default)
    {
        if (audio.IsEmpty)
        {
            return TimeSpan.Zero;
        }

        var seconds = await js.InvokeAsync<double>(Enqueue, ct, audio.Base64, audio.Format, pan);
        return TimeSpan.FromSeconds(seconds);
    }

    public ValueTask StopAsync(CancellationToken ct = default) => js.InvokeVoidAsync(Stop, ct);

    /// <summary>Seconds of audio still scheduled ahead of now — what a page waits on before advancing a round.</summary>
    public async Task<TimeSpan> PendingAsync(CancellationToken ct = default) =>
        TimeSpan.FromSeconds(await js.InvokeAsync<double>(Pending, ct));

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
    }
}
