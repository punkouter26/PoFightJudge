using Blazored.LocalStorage;
using Microsoft.JSInterop;

namespace PoFightJudge.Client.Services;

/// <summary>
/// The optional choices the Settings panel holds, each defaulting to the simplest version of the app. Kept in
/// LocalStorage like the theme; every read and write is guarded, because a blocked store means the defaults, not
/// a broken page.
/// </summary>
public sealed class Preferences(ILocalStorageService storage)
{
    /// <summary>The same key <c>js/theme.js</c>'s <c>PoMotion.still()</c> reads before any effect mounts.</summary>
    public const string EffectsKey = "po-effects";
    public const string On = "on";
    public const string Off = "off";

    /// <summary>Shaders, particles, terrain, stamps and foil. Off unless somebody turns them on.</summary>
    public bool Effects { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            Effects = string.Equals((await storage.GetItemAsStringAsync(EffectsKey))?.Trim('"'), On, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException)
        {
            // Storage blocked: the defaults stand.
        }
    }

    public async Task SetEffectsAsync(bool on)
    {
        Effects = on;
        try
        {
            await storage.SetItemAsStringAsync(EffectsKey, on ? On : Off);
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException)
        {
            // Not kept between visits; it still applies for this one.
        }
    }
}
