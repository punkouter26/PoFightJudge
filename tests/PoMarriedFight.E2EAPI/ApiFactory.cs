using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PoMarriedFight.Shared.Configuration;

namespace PoMarriedFight.E2EAPI;

/// <summary>
/// Hosts the API in-process under the <c>Test</c> environment: no Key Vault, fake credentials allowed, a placeholder
/// Gemini key so the "real" provider registration path is exercised, and (from T11) in-memory stores + the fakes.
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
            // No storage account in this host: the storage probe would make /api/health depend on Azurite. The
            // repositories are swapped for in-memory stores (T11+), so the check has nothing real to report here.
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
