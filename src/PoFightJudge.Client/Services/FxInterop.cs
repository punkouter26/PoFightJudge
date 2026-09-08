using Microsoft.JSInterop;

namespace PoFightJudge.Client.Services;

/// <summary>
/// The .NET side of <c>PoFx</c>: one-shot stage effects that are pure decoration. Every call is best-effort, because
/// a missing animation must never stop an argument — the page keeps running if the browser refuses.
/// </summary>
public sealed class FxInterop(IJSRuntime js)
{
    public const string Burst = "PoFx.burst";

    /// <summary>Plays the effect registered for an interjection key, if the browser has one.</summary>
    public async Task BurstAsync(string kind, CancellationToken ct = default)
    {
        try
        {
            await js.InvokeVoidAsync(Burst, ct, kind);
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
