using Blazored.LocalStorage;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Client.Services;

/// <summary>How a fight was set up last time, so the next one opens on the same card rather than on two blank boxes.</summary>
public sealed record FightCard(string One, string Two, string Topic, ProfileRole OneRole, ProfileRole TwoRole, HostPersonaId Persona);

/// <summary>The same for a watch: two personas, or a persona and the tag a person argued under.</summary>
public sealed record WatchCard(string Husband, string Wife, string Topic, string Tag = "");

/// <summary>
/// What the setup screens open on. Two things feed it, and the order matters: a rematch asked for just now outranks
/// whatever was set up last, and it is spent the moment it is read so pressing back does not silently re-arm it.
///
/// The remembered card lives in LocalStorage, which is per browser and per person — the same place the theme lives.
/// Every read and write is guarded: a private window, or a browser told to keep no site data, throws rather than
/// returning nothing, and a setup screen that cannot open because of a storage preference would be absurd.
/// </summary>
public sealed class SetupMemory(ILocalStorageService storage)
{
    public const string FightKey = "po-last-fight";
    public const string WatchKey = "po-last-watch";

    private FightCard? _pendingFight;
    private WatchCard? _pendingWatch;

    /// <summary>Arms a fight rematch. Read once by the setup screen, then gone.</summary>
    public void Rematch(FightCard card) => _pendingFight = card;

    /// <summary>Arms a watch rematch. Read once by the setup screen, then gone.</summary>
    public void Rematch(WatchCard card) => _pendingWatch = card;

    /// <summary>The card a fight setup should open on: a rematch if one is armed, else the last one played.</summary>
    public async Task<FightCard?> FightAsync()
    {
        if (_pendingFight is { } pending)
        {
            _pendingFight = null;
            return pending;
        }

        return await ReadAsync<FightCard>(FightKey);
    }

    public async Task<WatchCard?> WatchAsync()
    {
        if (_pendingWatch is { } pending)
        {
            _pendingWatch = null;
            return pending;
        }

        return await ReadAsync<WatchCard>(WatchKey);
    }

    public Task RememberAsync(FightCard card) => WriteAsync(FightKey, card);

    public Task RememberAsync(WatchCard card) => WriteAsync(WatchKey, card);

    private async Task<T?> ReadAsync<T>(string key)
        where T : class
    {
        try
        {
            return await storage.GetItemAsync<T>(key);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Microsoft.JSInterop.JSException or System.Text.Json.JsonException)
        {
            // No storage, or something older than the current shape sitting under the key. Open blank.
            return null;
        }
    }

    private async Task WriteAsync<T>(string key, T value)
    {
        try
        {
            await storage.SetItemAsync(key, value);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Microsoft.JSInterop.JSException)
        {
            // Storage is unavailable or full. Remembering the card is a convenience, never a reason to fail a start.
        }
    }
}
