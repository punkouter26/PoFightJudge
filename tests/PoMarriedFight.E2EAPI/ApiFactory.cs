using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PoMarriedFight.Api.Features.Profiles;
using PoMarriedFight.Shared.Configuration;
using PoMarriedFight.TestSupport;

namespace PoMarriedFight.E2EAPI;

/// <summary>
/// Hosts the API in-process under the <c>Test</c> environment: no Key Vault, fake credentials allowed, a placeholder
/// Gemini key so the "real" provider registration path is exercised, in-memory stores in place of Table/Blob storage,
/// and (from T15) the AI fakes.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string Environment = "Test";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environment);
        builder.UseSetting(ConfigKeys.KeyVault.Uri, string.Empty);
        builder.UseSetting(ConfigKeys.Ai.GeminiApiKey, "test-key");
        builder.UseSetting(ConfigKeys.Auth.AllowFakeAuth, "true");
        builder.UseSetting($"{Flags.Section}:{Flags.UseAzurite}", "false");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IProfileRepository>();
            services.AddSingleton<IProfileRepository, InMemoryProfileRepository>();
            services.RemoveAll<IProfileImageService>();
            services.AddSingleton<IProfileImageService, InMemoryProfileImageService>();

            // No storage account in this host: the storage probe would make /api/health depend on Azurite, and the
            // stores above never touch it, so the check has nothing real to report here.
            services.Configure<HealthCheckServiceOptions>(o =>
            {
                foreach (var registration in o.Registrations.Where(r => string.Equals(r.Name, "storage", StringComparison.Ordinal)).ToList())
                {
                    o.Registrations.Remove(registration);
                }
            });
        });
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "Api";
}
