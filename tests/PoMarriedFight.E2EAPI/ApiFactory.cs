using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
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
        builder.ConfigureTestServices(_ =>
        {
            // In-memory repositories and the AI fakes are registered here as their features arrive (T11+).
        });
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "Api";
}
