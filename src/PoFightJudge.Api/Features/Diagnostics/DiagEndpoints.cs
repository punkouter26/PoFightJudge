using Carter;
using Microsoft.FeatureManagement;
using PoFightJudge.Api.Common;
using PoFightJudge.Api.Features.Auth;
using PoFightJudge.Shared;
using PoFightJudge.Shared.Configuration;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Api.Features.Diagnostics;

/// <summary>
/// Authenticated diagnostics: environment, readiness, which providers are configured (presence, never values), the
/// model ids in use, the effective feature flags and the AI latency/token tables. Admin-only in Production (or anywhere when
/// <see cref="Flags.DiagRequiresAdminInDev"/> is on).
/// </summary>
public sealed class DiagEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet(ApiRoutes.Diag.Url, async (HttpContext ctx, IConfiguration config, IHostEnvironment env, StartupHealthState health, IFeatureManager features, AiLatencyTracker latency) =>
        {
            var adminOnly = env.IsProduction() || await features.IsEnabledAsync(Flags.DiagRequiresAdminInDev);
            if (adminOnly && !ctx.User.IsAdmin())
            {
                return Results.Forbid();
            }

            var flags = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var flag in Flags.All)
            {
                flags[flag] = await features.IsEnabledAsync(flag);
            }

            return Results.Ok(new DiagDto(
                env.EnvironmentName,
                health.IsDegraded ? "degraded" : "ok",
                health.MissingKeys,
                IsFakeAi(env, config, flags[Flags.UseFakeAi]),
                SecretMasker.Presence(config[ConfigKeys.Ai.GeminiApiKey]),
                SecretMasker.Presence(config[ConfigKeys.Ai.FishAudioApiKey]),
                SecretMasker.Presence(config[ConfigKeys.Ai.AzureSpeechKey]),
                SecretMasker.Presence(config[ConfigKeys.Storage.TableEndpoint]),
                SecretMasker.Presence(config[ConfigKeys.Storage.BlobEndpoint]),
                new ModelIdsDto(
                    config[ConfigKeys.Ai.RoundModel] ?? string.Empty,
                    config[ConfigKeys.Ai.JudgeModel] ?? string.Empty,
                    config[ConfigKeys.Ai.ProfileModel] ?? string.Empty,
                    config[ConfigKeys.Ai.TtsModel] ?? string.Empty,
                    config[ConfigKeys.Ai.LiveModel] ?? string.Empty,
                    config[ConfigKeys.Ai.TranscribeModel] ?? string.Empty,
                    config[ConfigKeys.Ai.Voice] ?? string.Empty),
                flags,
                latency.Snapshot(),
                latency.UsageSnapshot()));
        }).RequireAuthorization().WithTags("Diagnostics");

    /// <summary>
    /// The fakes run only outside Production, when forced by the flag or when no Gemini key is present. T15's AI
    /// registration makes the same decision; this stays the single formula both consult.
    /// </summary>
    public static bool IsFakeAi(IHostEnvironment env, IConfiguration config, bool useFakeFlag) =>
        !env.IsProduction() && (useFakeFlag || string.IsNullOrWhiteSpace(config[ConfigKeys.Ai.GeminiApiKey]));
}
