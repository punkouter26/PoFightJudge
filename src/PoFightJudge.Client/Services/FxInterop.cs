using Microsoft.JSInterop;

namespace PoFightJudge.Client.Services;

/// <summary>
/// The .NET side of <c>PoFx</c>: one-shot stage effects that are pure decoration. Every call is best-effort, because
/// a missing animation must never stop an argument — the page keeps running if the browser refuses.
/// </summary>
public sealed class FxInterop(IJSRuntime js)
{
    public const string Burst = "PoFx.burst";

    public const string Stamps = "PoFx.stamps";

    /// <summary>
    /// Arms every element matching <paramref name="selector"/> to stamp down onto the page the first time it is seen.
    /// Best-effort like the rest: a browser that will not simply shows them where they are.
    /// </summary>
    public async Task StampsAsync(string selector, CancellationToken ct = default)
    {
        try
        {
            await js.InvokeVoidAsync(Stamps, ct, selector);
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or TaskCanceledException)
        {
            // Nothing armed, so nothing waits: they are on the page as they were.
        }
    }

    /// <summary>Plays the effect registered for an interjection key, if the browser has one.</summary>
    public Task BurstAsync(string kind, CancellationToken ct = default) => BurstAsync(kind, null, ct);

    /// <summary>The same, on the first element matching <paramref name="selector"/> rather than the stage.</summary>
    public async Task BurstAsync(string kind, string? selector, CancellationToken ct = default)
    {
        try
        {
            await js.InvokeVoidAsync(Burst, ct, kind, selector);
        }
        catch (JSException)
        {
            // The effect is decoration; a browser that will not play it changes nothing about the match.
        }
        catch (InvalidOperationException)
        {
            // Prerendering: there is no browser to play it in yet.
        }
        catch (TaskCanceledException)
        {
            // The page moved on before the effect started.
        }
    }
}
