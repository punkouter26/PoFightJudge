using Carter;
using Microsoft.FeatureManagement;
using PoMarriedFight.Api.Features.Voice;
using PoMarriedFight.Shared;
using PoMarriedFight.Shared.Configuration;
using PoMarriedFight.Shared.Models;

namespace PoMarriedFight.Api.Features.Diagnostics;

/// <summary>
/// Effective feature flags for the WASM client (anonymous — the Login page reads it to decide whether to offer the
/// guest button, and the banner reads <c>UseFakeAi</c>). Environment rules are applied here so the client never has
/// to know them: the fakes and the guest door are impossible in Production whatever the configuration says.
/// </summary>
public sealed class FeatureFlagEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet(ApiRoutes.Features.Url, async (IConfiguration config, IHostEnvironment env, IFeatureManager features, ITranscriptionService transcription) =>
            Results.Ok(new FeatureFlagsDto(
                UseFakeAi: DiagEndpoints.IsFakeAi(env, config, await features.IsEnabledAsync(Flags.UseFakeAi)),
                DevGuestEnabled: !env.IsProduction() && await features.IsEnabledAsync(Flags.DevGuestEnabled),
                // The server half of the gate on a person taking a side in WATCH; the client ANDs it with its own
                // microphone check. A spoken turn is only offered when a transcriber can actually finish it.
                HumanInWatch: await features.IsEnabledAsync(Flags.HumanInWatch) && transcription.IsEnabled,
                BrowserSpeechRecognition: await features.IsEnabledAsync(Flags.BrowserSpeechRecognition))))
            .AllowAnonymous().WithTags("Diagnostics");
}
