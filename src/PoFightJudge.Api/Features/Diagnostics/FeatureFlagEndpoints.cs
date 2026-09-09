using Carter;
using PoFightJudge.Api.Features.Voice;
using PoFightJudge.Shared;
using PoFightJudge.Shared.Configuration;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Api.Features.Diagnostics;

/// <summary>
/// The four answers the WASM client cannot work out for itself — whether the fakes are speaking, whether the guest
/// door is open, whether a person can take a side in WATCH, and whether the dev Microsoft door is wired. Anonymous,
/// because the Login page reads it before anybody has signed in. Every one is derived here from the environment and
/// the configuration, so the client never has to know the rules.
/// </summary>
public sealed class FeatureFlagEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet(ApiRoutes.Features.Url, (IConfiguration config, IHostEnvironment env, ITranscriptionService transcription) =>
            Results.Ok(new FeatureFlagsDto(
                UseFakeAi: DiagEndpoints.IsFakeAi(env, config),

                // The guest door is impossible in Production whatever the configuration says.
                DevGuestEnabled: !env.IsProduction(),

                // A spoken turn is only offered when a transcriber can actually finish it. The client ANDs this with
                // its own microphone check.
                HumanInWatch: transcription.IsEnabled,

                // Dev Entra: Development, the AllowDevEntra setting, and a dev client id. The client id check is what
                // makes the gate fail-closed if Key Vault is unavailable locally.
                DevEntraEnabled: env.IsDevelopment()
                    && config.GetValue<bool>(ConfigKeys.Auth.AllowDevEntra)
                    && !string.IsNullOrWhiteSpace(config[ConfigKeys.Auth.DevClientId]))))
            .AllowAnonymous().WithTags("Diagnostics");
}
