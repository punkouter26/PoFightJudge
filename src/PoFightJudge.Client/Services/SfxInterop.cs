using Blazored.LocalStorage;
using Microsoft.JSInterop;

namespace PoFightJudge.Client.Services;

/// <summary>
/// The names of the synthesized effects, so a call site names a sound rather than spelling one. Every name here has
/// a voice in <c>js/sfx.js</c>, and a test holds the two lists together — a page asking for a sound the browser has
/// never heard of would simply be silent, which is exactly the kind of quiet nobody investigates.
/// </summary>
public static class Sfx
{
    /// <summary>One ring: a round is starting.</summary>
    public const string Bell = "bell";

    /// <summary>Three rings: the argument is over.</summary>
    public const string BellThree = "bell3";

    /// <summary>The slap landing.</summary>
    public const string Slap = "slap";

    public const string Gavel = "gavel";

    /// <summary>Three strikes under a ruling.</summary>
    public const string GavelThree = "gavel3";

    /// <summary>The quietest thing here: a confirmation, not an announcement.</summary>
    public const string Tick = "tick";

    /// <summary>Something moving on screen — a bar filling, a card arriving.</summary>
    public const string Whoosh = "whoosh";

    /// <summary>The fight is being set up.</summary>
    public const string Intro = "stinger-intro";

    /// <summary>The host has started questioning them.</summary>
    public const string Probe = "stinger-probe";

    /// <summary>The host is about to rule.</summary>
    public const string Ruling = "stinger-verdict";

    /// <summary>Somebody won.</summary>
    public const string Fanfare = "fanfare";

    /// <summary>The microphone is peaking and whatever they are saying will be mangled.</summary>
    public const string Clipping = "clipping";

    /// <summary>T111: the wax-seal stamp landing on a fallacy.</summary>
    public const string Thump = "thump";

    /// <summary>A prompt: it is your turn and nobody has said anything for a while.</summary>
    public const string Cue = "cue";

    /// <summary>T114: a three-note motif that lands just before the persona preview line.</summary>
    public const string Chord = "chord";

    /// <summary>A counting scoreboard, one per tick. Takes a 0–1 value and climbs with it.</summary>
    public const string Count = "count";

    /// <summary>Every name above, for the test that checks the browser knows them all.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Bell, BellThree, Slap, Gavel, GavelThree, Tick, Whoosh,
        Intro, Probe, Ruling, Fanfare, Clipping, Thump, Cue, Chord, Count,
    ];
}

/// <summary>
/// The .NET side of <c>PoSfx</c>. Like <see cref="FxInterop"/>, every call is best-effort: a sound that will not
/// play changes nothing about the match, and a page must never stop because a browser refused to make a noise.
/// </summary>
/// <remarks>
/// The choice is stored under the same key <c>js/sfx.js</c> reads for itself at load. That duplication is on
/// purpose: silence cannot be applied retroactively, so the browser has to know before .NET can tell it.
/// </remarks>
public sealed class SfxInterop(IJSRuntime js, ILocalStorageService storage)
{
    public const string Play = "PoSfx.play";
    public const string Arm = "PoSfx.arm";
    public const string SetMuted = "PoSfx.setMuted";
    public const string Duck = "PoSfx.duck";

    public const string StorageKey = "po-sound";
    public const string On = "on";
    public const string Off = "off";

    /// <summary>Sound is on unless somebody has turned it off: this app talks out loud by design.</summary>
    public bool IsOn { get; private set; } = true;

    /// <summary>Reads the stored choice and tells the browser about it. Safe when storage is blocked.</summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        try
        {
            var stored = await storage.GetItemAsStringAsync(StorageKey, ct);
            IsOn = !string.Equals(stored?.Trim('"'), Off, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException)
        {
            // Storage blocked or not ready — sound stays on, which is what js/sfx.js decided too.
        }

        await ApplyAsync(IsOn, persist: false, ct);
    }

    public Task ToggleAsync(CancellationToken ct = default) => ApplyAsync(!IsOn, persist: true, ct);

    private async Task ApplyAsync(bool on, bool persist, CancellationToken ct)
    {
        IsOn = on;
        await SafelyAsync(() => js.InvokeVoidAsync(SetMuted, ct, !on));
        if (!persist)
        {
            return;
        }

        try
        {
            await storage.SetItemAsStringAsync(StorageKey, on ? On : Off, ct);
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException)
        {
            // Applied for this page at least; it will not be remembered for the next one.
        }
    }

    /// <summary>
    /// Wakes the audio context on a user gesture. Worth calling from the click that starts something rather than
    /// leaving the first bell to be swallowed by the browser's autoplay policy.
    /// </summary>
    public Task ArmAsync(CancellationToken ct = default) => SafelyAsync(() => js.InvokeVoidAsync(Arm, ct));

    /// <summary>Plays one effect by name. <paramref name="value"/> is 0–1 for the voices that take one.</summary>
    public Task PlayAsync(string name, double value = 0, CancellationToken ct = default) =>
        SafelyAsync(() => js.InvokeVoidAsync(Play, ct, name, value));

    /// <summary>Dips whatever voice is speaking for a moment, so an effect lands over the top of it.</summary>
    public Task DuckAsync(double seconds, CancellationToken ct = default) =>
        SafelyAsync(() => js.InvokeVoidAsync(Duck, ct, seconds));

    /// <summary>Plays an effect over the top of whoever is talking: the duck and the sound go together.</summary>
    public async Task PlayOverAsync(string name, double duckSeconds, CancellationToken ct = default)
    {
        await DuckAsync(duckSeconds, ct);
        await PlayAsync(name, 0, ct);
    }

    private static async Task SafelyAsync(Func<ValueTask> call)
    {
        try
        {
            await call();
        }
        catch (JSException)
        {
            // The browser would not play it. The app is unaffected.
        }
        catch (InvalidOperationException)
        {
            // Prerendering: there is no browser to play it in yet.
        }
        catch (TaskCanceledException)
        {
            // The page moved on before the sound started.
        }
    }
}
