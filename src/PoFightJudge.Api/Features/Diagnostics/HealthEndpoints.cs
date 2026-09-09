using Carter;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.FeatureManagement;
using PoFightJudge.Api.Common;
using PoFightJudge.Shared;
using PoFightJudge.Shared.Configuration;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Api.Features.Diagnostics;

/// <summary>
/// Health surface. <c>/api/health</c> and <c>/healthz</c> run only the <c>ready</c> checks (configuration, storage)
/// — no network probes, so the deploy gate and the tests never depend on a third party being up.
/// <c>/api/health/details</c> adds the <c>ai</c> reachability probes and a masked configuration/feature report for
/// the Blazor /health page. All anonymous; all presence-only.
/// </summary>
public sealed class HealthEndpoints : ICarterModule
{
    public const string ReadyTag = "ready";
    public const string AiTag = "ai";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var ready = new HealthCheckOptions { Predicate = h => h.Tags.Contains(ReadyTag) };
        app.MapHealthChecks(ApiRoutes.Health.Url, ready).AllowAnonymous().WithTags("Health");
        app.MapHealthChecks(ApiRoutes.Health.ProbeUrl, ready).AllowAnonymous();
        app.MapHealthChecks(ApiRoutes.Health.ReadyProbeUrl, ready).AllowAnonymous();

        app.MapGet(ApiRoutes.Health.DetailsUrl, async (HealthCheckService health, IConfiguration config, IHostEnvironment env, IFeatureManager features, TimeProvider clock, CancellationToken ct) =>
        {
            var report = await health.CheckHealthAsync(ct);
            var checks = new List<HealthCheckDto>();

            foreach (var (name, entry) in report.Entries)
            {
                checks.Add(new HealthCheckDto(name, Map(entry.Status), entry.Description ?? entry.Status.ToString(), HealthCategory.Connection));
            }

            AddPresence(checks, "Gemini API key", config[ConfigKeys.Ai.GeminiApiKey], required: env.IsProduction());
            AddPresence(checks, "Entra client id", config[ConfigKeys.AzureAd.ClientId], required: env.IsProduction());
            checks.Add(new HealthCheckDto("Key Vault", string.IsNullOrWhiteSpace(config[ConfigKeys.KeyVault.Uri]) ? HealthState.NotConfigured : HealthState.Ok, SecretMasker.MaskHost(config[ConfigKeys.KeyVault.Uri]), HealthCategory.Configuration));
            checks.Add(new HealthCheckDto("Table endpoint", string.IsNullOrWhiteSpace(config[ConfigKeys.Storage.TableEndpoint]) ? HealthState.Failed : HealthState.Ok, SecretMasker.MaskHost(config[ConfigKeys.Storage.TableEndpoint]), HealthCategory.Configuration));
            checks.Add(new HealthCheckDto("Blob endpoint", string.IsNullOrWhiteSpace(config[ConfigKeys.Storage.BlobEndpoint]) ? HealthState.Failed : HealthState.Ok, SecretMasker.MaskHost(config[ConfigKeys.Storage.BlobEndpoint]), HealthCategory.Configuration));

            foreach (var flag in Flags.All)
            {
                checks.Add(new HealthCheckDto($"Feature: {flag}", HealthState.Ok, (await features.IsEnabledAsync(flag)) ? "on" : "off", HealthCategory.Feature));
            }

            return Results.Ok(new HealthReportDto(env.EnvironmentName, Aggregate(checks), clock.GetUtcNow(), checks));
        }).AllowAnonymous().WithTags("Health");
    }

    private static void AddPresence(List<HealthCheckDto> checks, string name, string? value, bool required)
    {
        var present = !string.IsNullOrWhiteSpace(value);
        var state = present ? HealthState.Ok : required ? HealthState.Failed : HealthState.NotConfigured;
        checks.Add(new HealthCheckDto(name, state, present ? SecretMasker.Mask(value) : "not set", HealthCategory.Configuration));
    }

    private static HealthState Map(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => HealthState.Ok,
        HealthStatus.Degraded => HealthState.Degraded,
        _ => HealthState.Failed,
    };

    private static HealthState Aggregate(IReadOnlyList<HealthCheckDto> checks) =>
        checks.Any(c => c.State == HealthState.Failed) ? HealthState.Failed
        : checks.Any(c => c.State == HealthState.Degraded) ? HealthState.Degraded
        : HealthState.Ok;
}

/// <summary>
/// Reports unhealthy when <see cref="StartupSecretValidator"/> found required configuration missing in Production.
/// The same fact is on <c>/api/diag</c>, but that route is authenticated, so a deploy pipeline cannot see it: without
/// this check a site with no Gemini key answers 200 on every route and only fails once someone starts a fight.
/// Names, never values — the keys are already public constants.
/// </summary>
public sealed class ConfigurationHealthCheck(StartupHealthState state) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(state.IsDegraded
            ? HealthCheckResult.Unhealthy($"Required configuration missing: {string.Join(", ", state.MissingKeys)}")
            : HealthCheckResult.Healthy("Required configuration present."));
}

public static class DiagnosticsServiceExtensions
{
    /// <summary>
    /// Registers the health checks. <c>ready</c>: configuration and storage. <c>ai</c>: TLS reachability of
    /// the AI hosts — any HTTP answer counts, because the point is DNS + TLS, not an authenticated call; failures only
    /// degrade, never fail, so an upstream blip cannot take the site out of rotation.
    /// </summary>
    public static IServiceCollection AddPoDiagnostics(this IServiceCollection services, IConfiguration configuration)
    {
        var checks = services.AddHealthChecks()
            .AddCheck<ConfigurationHealthCheck>("configuration", tags: [ReadyTagName]);

        AddReachability(checks, "gemini-reachable", "https://generativelanguage.googleapis.com/");

        return services;
    }

    private const string ReadyTagName = HealthEndpoints.ReadyTag;

    private static void AddReachability(IHealthChecksBuilder checks, string name, string url) =>
        checks.AddUrlGroup(
            o => o.AddUri(new Uri(url), s => s.ExpectHttpCodes(200, 499)),
            name: name,
            failureStatus: HealthStatus.Degraded,
            tags: [HealthEndpoints.AiTag],
            timeout: TimeSpan.FromSeconds(5));
}
