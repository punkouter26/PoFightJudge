using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PoFightJudge.Api.Features.Fighters;
using PoFightJudge.Api.Features.Profiles;
using PoFightJudge.Api.Features.Records;
using PoFightJudge.Api.Features.Storage;
using PoFightJudge.Api.Features.Watch;
using PoFightJudge.Shared.Configuration;
using PoFightJudge.TestSupport;

namespace PoFightJudge.E2EAPI;

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
        // No storage account in this host, so the blob TTS cache would spend its retry budget on every call.
        builder.UseSetting($"{Flags.Section}:{Flags.TtsCacheEnabled}", "false");
        // The key above exercises the real registration path; the flag still routes every AI call to the deterministic fakes.
        builder.UseSetting($"{Flags.Section}:{Flags.UseFakeAi}", "true");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IProfileRepository>();
            services.AddSingleton<IProfileRepository, InMemoryProfileRepository>();
            services.RemoveAll<IProfileImageService>();
            services.AddSingleton<IProfileImageService, InMemoryProfileImageService>();
            services.RemoveAll<IWatchResultRepository>();
            services.AddSingleton<IWatchResultRepository, InMemoryWatchResultRepository>();
            services.RemoveAll<IFighterResultRepository>();
            services.AddSingleton<IFighterResultRepository, InMemoryFighterResultRepository>();
            services.RemoveAll<IFighterRepository>();
            services.AddSingleton<IFighterRepository, InMemoryFighterRepository>();
            services.RemoveAll<IFighterWordsRepository>();
            services.AddSingleton<IFighterWordsRepository, InMemoryFighterWordsRepository>();
            services.RemoveAll<IMatchRepository>();
            services.AddSingleton<IMatchRepository, InMemoryMatchRepository>();
            services.AddSingleton<IShareRepository, InMemoryShareRepository>();
            services.RemoveAll<IWatchAudioStore>();
            services.AddSingleton<IWatchAudioStore, InMemoryWatchAudioStore>();

            // A fight's recording and its transcripts, so the analysis has something real to read without Azure.
            services.RemoveAll<IAudioBlobStore>();
            services.AddSingleton<IAudioBlobStore, InMemoryAudioBlobStore>();

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
