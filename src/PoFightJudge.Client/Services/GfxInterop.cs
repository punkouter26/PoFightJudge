using Microsoft.JSInterop;

namespace PoFightJudge.Client.Services;

/// <summary>The shaders <c>js/gfx.js</c> knows how to draw. A name that is not there mounts nothing.</summary>
public static class Shaders
{
    /// <summary>The watch stage: an aura under whoever is speaking, and the ring a slap sends out.</summary>
    public const string Stage = "stage";

    /// <summary>The live fight's backdrop: the two fighters' colours flowing, heating up as the clock runs out.</summary>
    public const string Backdrop = "backdrop";

    /// <summary>Every name above, for the test that checks the browser knows them all.</summary>
    public static IReadOnlyList<string> All { get; } = [Stage, Backdrop];
}

/// <summary>
/// The .NET side of <c>PoGfx</c>: a WebGL2 canvas mounted on an element the page already lays out. Every call is
/// best-effort and every answer is allowed to be "no" — a browser without WebGL2, or somebody who has asked for
/// less motion, simply keeps the CSS the markup already had.
/// </summary>
/// <remarks>
/// Nothing is drawn from here. A page mounts a shader, tells it which way to point and when something happened;
/// the frames themselves are the browser's business, because sixty of them a second is not a conversation to have
/// across the interop boundary.
/// </remarks>
public sealed class GfxInterop(IJSRuntime js)
{
    public const string Mount = "PoGfx.mount";
    public const string Unmount = "PoGfx.unmount";
    public const string Focus = "PoGfx.focus";
    public const string Shock = "PoGfx.shock";
    public const string Set = "PoGfx.set";

    /// <summary>Which audio bus a shader breathes with: the watch player, the live host, or neither.</summary>
    public const string WatchLevel = "watch";
    public const string LiveLevel = "live";

    /// <summary>Mounts a shader on the first element matching <paramref name="selector"/>. False when it could not be.</summary>
    public async Task<bool> MountAsync(string selector, string shader, string? level = null, CancellationToken ct = default)
    {
        try
        {
            return await js.InvokeAsync<bool>(Mount, ct, selector, shader, new { level });
        }
        catch (JSException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }

    public Task UnmountAsync(string selector, CancellationToken ct = default) =>
        SafelyAsync(() => js.InvokeVoidAsync(Unmount, ct, selector));

    /// <summary>Points the effect at a child of the mounted element — the card of whoever is speaking.</summary>
    public Task FocusAsync(string selector, string? childSelector, CancellationToken ct = default) =>
        SafelyAsync(() => js.InvokeVoidAsync(Focus, ct, selector, childSelector));

    /// <summary>Sends a ring out from the middle of the mounted element.</summary>
    public Task ShockAsync(string selector, CancellationToken ct = default) =>
        SafelyAsync(() => js.InvokeVoidAsync(Shock, ct, selector));

    /// <summary>One numeric uniform the page drives: <c>side</c>, <c>mix</c> or <c>heat</c>.</summary>
    public Task SetAsync(string selector, string name, double value, CancellationToken ct = default) =>
        SafelyAsync(() => js.InvokeVoidAsync(Set, ct, selector, name, value));

    private static async Task SafelyAsync(Func<ValueTask> call)
    {
        try
        {
            await call();
        }
        catch (JSException)
        {
            // Nothing is mounted, or the browser refused. The page is unaffected.
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
