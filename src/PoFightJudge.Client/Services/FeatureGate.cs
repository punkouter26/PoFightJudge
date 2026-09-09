using PoFightJudge.Shared.Models;

namespace PoFightJudge.Client.Services;

/// <summary>
/// What the server can actually do, read once before the app renders.
///
/// Four places used to ask for the flags themselves, which was four requests for one answer and, worse, an answer
/// that arrived after first paint: <see cref="Components.FakeAiBanner"/> renders a strip above the top bar, so every
/// page load settled and then pushed the whole app down by its height. Loading it in Program.cs, between building
/// the host and running it, means <see cref="Flags"/> is already there on the first render and nothing moves.
///
/// A failed load is not fatal. Every flag defaults to off, which is the honest answer when the API is not talking:
/// no fake-AI banner, no dev guest, no spoken turn.
/// </summary>
public sealed class FeatureGate(IApiClient api)
{
    private static readonly FeatureFlagsDto Off = new(UseFakeAi: false, DevGuestEnabled: false, HumanInWatch: false, BrowserSpeechRecognition: false);

    /// <summary>The flags. Never null: it is <see cref="Off"/> until the load succeeds, and again if it does not.</summary>
    public FeatureFlagsDto Flags { get; private set; } = Off;

    /// <summary>True once the server has answered, so a page can tell "off" from "not asked yet" if it needs to.</summary>
    public bool Loaded { get; private set; }

    /// <summary>Called once, from Program.cs, before the first render. Safe to call again; it simply asks again.</summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            // Coalesced even though the contract says non-null: every component reads Flags synchronously, so one
            // null here is a NullReferenceException on a page rather than a flag that is off.
            Flags = await api.GetFeaturesAsync(ct) ?? Off;
            Loaded = true;
        }
        catch (Exception ex) when (ex is HttpRequestException or ApiException or TaskCanceledException)
        {
            // The API is down or slow. The pages say so in their own words; the flags stay off.
            Flags = Off;
        }
    }
}
