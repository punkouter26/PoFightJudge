using Azure.Core;
using Azure.Identity;
using Carter;
using FluentValidation;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.FeatureManagement;
using PoMarriedFight.Api.Common;
using PoMarriedFight.Api.Features.Ai;
using PoMarriedFight.Api.Features.Analysis;
using PoMarriedFight.Api.Features.Auth;
using PoMarriedFight.Api.Features.Diagnostics;
using PoMarriedFight.Api.Features.Fight;
using PoMarriedFight.Api.Features.Fighters;
using PoMarriedFight.Api.Features.Profiles;
using PoMarriedFight.Api.Features.Records;
using PoMarriedFight.Api.Features.Voice;
using PoMarriedFight.Api.Features.Watch;
using PoMarriedFight.Shared;
using PoMarriedFight.Shared.Configuration;
using PoMarriedFight.Shared.Validators;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;

// Bootstrap logger so Key Vault / startup problems are visible before the full Serilog configuration is read.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // ── Key Vault: PoMarriedFight--* secrets only ─────────────────────────────────────────────────────────────────
    // DefaultAzureCredential = `az login` locally, managed identity in Azure. Outside Development the slow interactive
    // providers are excluded so a missing identity fails in seconds, not minutes. KV failure is logged, never fatal:
    // StartupSecretValidator decides what "missing" means per environment.
    var kvUri = builder.Configuration[ConfigKeys.KeyVault.Uri];
    if (!string.IsNullOrWhiteSpace(kvUri))
    {
        try
        {
            TokenCredential credential = builder.Environment.IsDevelopment()
                ? new DefaultAzureCredential()
                : new DefaultAzureCredential(new DefaultAzureCredentialOptions
                {
                    ExcludeVisualStudioCredential = true,
                    ExcludeVisualStudioCodeCredential = true,
                    ExcludeAzureCliCredential = true,
                    ExcludeAzurePowerShellCredential = true,
                    ExcludeAzureDeveloperCliCredential = true,
                    ExcludeInteractiveBrowserCredential = true,
                });
            builder.Configuration.AddAzureKeyVault(new Uri(kvUri), credential, new PoMarriedFightSecretManager());
            Log.Information("Key Vault configured: {Uri}", kvUri);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warning(ex, "Key Vault unavailable — continuing without it. Uri={Uri}", kvUri);
        }
    }

    // GEMINI_API_KEY / FISH_API_KEY are accepted aliases; fold them into the canonical keys so diag, validation and
    // clients all agree on one source of truth.
    foreach (var (key, envVar) in new[] { (ConfigKeys.Ai.GeminiApiKey, ConfigKeys.Ai.GeminiApiKeyEnvVar), (ConfigKeys.Ai.FishAudioApiKey, ConfigKeys.Ai.FishAudioApiKeyEnvVar) })
    {
        if (string.IsNullOrWhiteSpace(builder.Configuration[key]) && Environment.GetEnvironmentVariable(envVar) is { Length: > 0 } fromEnv)
        {
            builder.Configuration[key] = fromEnv;
        }
    }

    // ── Local Azurite override ────────────────────────────────────────────────────────────────────────────────────
    // After Key Vault on purpose: configuration is last-wins and KV carries the real dev endpoints. Swaps the ENDPOINTS
    // only — Azurite runs `--oauth basic` over HTTPS, so the same DefaultAzureCredential flow authenticates against it.
    if (builder.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>($"{Flags.Section}:{Flags.UseAzurite}"))
    {
        var table = builder.Configuration[ConfigKeys.Azurite.TableEndpoint];
        var blob = builder.Configuration[ConfigKeys.Azurite.BlobEndpoint];
        if (string.IsNullOrWhiteSpace(table) || string.IsNullOrWhiteSpace(blob))
        {
            Log.Error("{Flag} is on but {TableKey}/{BlobKey} are not set — falling back to the REAL storage endpoints", Flags.UseAzurite, ConfigKeys.Azurite.TableEndpoint, ConfigKeys.Azurite.BlobEndpoint);
        }
        else
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                [ConfigKeys.Storage.TableEndpoint] = table,
                [ConfigKeys.Storage.BlobEndpoint] = blob,
            });
            Log.Information("Azurite ON — storage endpoints overridden to {Table} / {Blob} (SCRIPTS/azurite.ps1)", table, blob);
        }
    }

    // ── Serilog ───────────────────────────────────────────────────────────────────────────────────────────────────
    builder.Host.UseSerilog((ctx, services, cfg) =>
    {
        cfg.ReadFrom.Configuration(ctx.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithEnvironmentName()
            .Enrich.WithProperty("Application", ConfigKeys.Root)
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");

        // Seq is a local structured-log viewer (docker-compose). Development only; the sink buffers if Seq is down.
        if (ctx.HostingEnvironment.IsDevelopment() && ctx.Configuration[ConfigKeys.Telemetry.SeqServerUrl] is { Length: > 0 } seq)
        {
            cfg.WriteTo.Seq(seq);
        }
    });

    // ── Services ──────────────────────────────────────────────────────────────────────────────────────────────────
    var fakeAuth = GuestMiddleware.IsEnabledIn(builder.Environment, builder.Configuration);
    builder.Services.AddPoAuth(builder.Configuration, builder.Environment, fakeAuth);
    builder.Services.AddSingleton<StartupHealthState>();
    builder.Services.AddPoDiagnostics(builder.Configuration);
    builder.Services.AddPoStorage(builder.Configuration, builder.Environment);
    builder.Services.AddScoped<IProfileRepository, ProfileRepository>();
    builder.Services.AddSingleton<IProfileImageService, ProfileImageService>();
    builder.Services.AddSingleton<IProfileGenerator, ProfileGenerator>();
    builder.Services.AddScoped<IMatchRepository, MatchRepository>();
    builder.Services.AddScoped<IFighterRepository, FighterRepository>();
    builder.Services.AddScoped<IFighterResultRepository, FighterResultRepository>();
    builder.Services.AddScoped<IWatchResultRepository, WatchResultRepository>();
    builder.Services.AddPoWatch();
    builder.Services.AddValidatorsFromAssemblyContaining<CreateProfileRequestValidator>(ServiceLifetime.Singleton);
    builder.Services.AddHostedService<StartupSecretValidator>();
    var ai = builder.Services.AddPoAi(builder.Configuration, builder.Environment);
    builder.Services.AddPoVoice(builder.Configuration, ai);
    builder.Services.AddPoFight(builder.Configuration, ai);
    builder.Services.AddPoAnalysis(builder.Configuration, ai);
    Log.Information("AI providers: {Mode} ({Reason})", ai.UseFakes ? "fakes" : "real", ai.Reason);
    builder.Services.TryAddSingleton(TimeProvider.System);
    builder.Services.AddFeatureManagement();
    builder.Services.AddHybridCache();
    builder.Services.AddCarter();
    builder.Services.AddOpenApi();
    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<BadRequestExceptionHandler>();
    builder.Services.Configure<JsonOptions>(o => o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);

    var app = builder.Build();

    // ── Pipeline ──────────────────────────────────────────────────────────────────────────────────────────────────
    // Request logging sits outside the exception handler so the line records the status the caller actually got.
    // The other way round, a malformed request that the handler answers with a 400 was logged as a 500.
    app.UseSerilogRequestLogging();
    app.UseExceptionHandler();
    app.UseMiddleware<SecurityHeadersMiddleware>();
    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi().AllowAnonymous();
        app.MapScalarApiReference(o => o.WithTitle("PoMarriedFight API")).AllowAnonymous();
    }
    else
    {
        app.UseHsts();
        app.UseHttpsRedirection();
    }

    app.UseBlazorFrameworkFiles();
    // Outside Production nothing the client serves may be cached: every Po* solution runs on https://localhost:5001, so a
    // sibling app's js/audio.js would otherwise be served to this one from the shared origin.
    var staticFiles = new StaticFileOptions
    {
        OnPrepareResponse = app.Environment.IsProduction()
            ? static _ => { }
        : static ctx =>
        {
            ctx.Context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            ctx.Context.Response.Headers.Pragma = "no-cache";
        },
    };
    app.UseStaticFiles(staticFiles);

    // A missing file-like path (/nope.txt, a browsers /favicon.ico) is a 404. Without this the fallback authorization
    // policy challenges even endpoint-less requests and every such miss became a 401. A middleware, not an endpoint: a
    // matched endpoint would stop StaticFileMiddleware from serving the assets that do exist.
    app.Use(async (ctx, next) =>
    {
        if (ctx.GetEndpoint() is null && Path.HasExtension(ctx.Request.Path.Value))
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await next();
    });

    app.UseAuthentication();
    if (fakeAuth)
    {
        app.UseMiddleware<GuestMiddleware>();
    }

    app.UseAuthorization();

    // After authorization, so the AI limit partitions by the caller rather than by connection.
    app.UseRateLimiter();

    // Degraded gate: in Production with missing config every /api route except diag answers 503 with the missing keys.
    var health = app.Services.GetRequiredService<StartupHealthState>();
    app.Use(async (ctx, next) =>
    {
        var path = ctx.Request.Path;
        if (health.IsDegraded && path.StartsWithSegments(ApiRoutes.ApiPrefix, StringComparison.OrdinalIgnoreCase) && !path.StartsWithSegments(ApiRoutes.Diag.Url, StringComparison.OrdinalIgnoreCase))
        {
            ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await ctx.Response.WriteAsJsonAsync(new { status = "degraded", missing = health.MissingKeys });
            return;
        }

        await next();
    });

    // Every feature is an ICarterModule; nothing is mapped by hand here.
    app.MapCarter();

    // Unknown /api/* routes must be a clean 404, never the SPA fallback (which would answer 200 + index.html).
    app.MapFallback($"{ApiRoutes.ApiPrefix}/{{**path}}", () => Results.NotFound(new { error = "No such API route." })).AllowAnonymous();
    app.MapFallbackToFile("index.html", staticFiles).AllowAnonymous();

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "PoMarriedFight terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>Exposed for WebApplicationFactory in tests.</summary>
public partial class Program;
