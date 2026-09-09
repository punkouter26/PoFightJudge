using Microsoft.JSInterop;

namespace PoFightJudge.Client.Services;

/// <summary>
/// The .NET side of <c>PoParticles</c>. Like the rest of the effects layer, nothing here is load-bearing: a page
/// that could not throw any confetti has still shown somebody who won.
/// </summary>
public sealed class ParticleInterop(IJSRuntime js)
{
    public const string Burst = "PoParticles.burst";
    public const string Emit = "PoParticles.emit";
    public const string Clear = "PoParticles.clear";

    /// <summary>A hit: hot, fast and short, thrown out from wherever it landed.</summary>
    public const string Sparks = "sparks";

    /// <summary>A win: slow, broad and tumbling, from above.</summary>
    public const string Confetti = "confetti";

    /// <summary>
    /// Throws a burst into an element, optionally centred on a child of it — the browser measures where that child
    /// actually is, so it follows the layout rather than assuming one.
    /// </summary>
    public Task BurstAsync(string selector, string kind, string? at = null, CancellationToken ct = default) =>
        SafelyAsync(() => js.InvokeVoidAsync(Burst, ct, selector, kind, at));

    /// <summary>Embers, per second. Zero stops them; the canvas leaves once the last one has died.</summary>
    public Task EmitAsync(string selector, double perSecond, CancellationToken ct = default) =>
        SafelyAsync(() => js.InvokeVoidAsync(Emit, ct, selector, perSecond));

    /// <summary>Stops everything and takes the canvas down now, rather than when the last particle dies.</summary>
    public Task ClearAsync(string selector, CancellationToken ct = default) =>
        SafelyAsync(() => js.InvokeVoidAsync(Clear, ct, selector));

    /// <summary>The same, for a page that can only dispose synchronously. See <see cref="GfxInterop.Release"/>.</summary>
    public void Release(string selector) => _ = ClearAsync(selector, CancellationToken.None);

    private static async Task SafelyAsync(Func<ValueTask> call)
    {
        try
        {
            await call();
        }
        catch (JSException)
        {
            // The browser would not draw it. Nothing else changes.
        }
        catch (InvalidOperationException)
        {
            // Prerendering: there is no canvas yet.
        }
        catch (TaskCanceledException)
        {
            // The page moved on.
        }
    }
}
