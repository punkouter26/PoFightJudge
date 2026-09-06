using Azure.Core;
using Azure.Data.Tables;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.FeatureManagement;
using PoMarriedFight.Api.Features.Diagnostics;
using PoMarriedFight.Api.Features.Storage;
using PoMarriedFight.Shared.Configuration;

namespace PoMarriedFight.Api.Common;

/// <summary>Container names, bound from <c>PoMarriedFight:Storage</c>.</summary>
public sealed record StorageContainers(string AudioContainer, string FacesContainer, string TtsCacheContainer)
{
    public static StorageContainers From(IConfiguration configuration) => new(
        configuration[ConfigKeys.Storage.AudioContainer] ?? "pomarriedfight-audio",
        configuration[ConfigKeys.Storage.FacesContainer] ?? "pomarriedfight-faces",
        configuration[ConfigKeys.Storage.TtsCacheContainer] ?? "pomarriedfight-ttscache");
}

public static class StorageServiceExtensions
{
    /// <summary>
    /// Identity-only storage: both clients bind to an endpoint URI and authenticate with one shared
    /// <see cref="DefaultAzureCredential"/> — managed identity in Azure, <c>az login</c> locally, and the same flow
    /// against Azurite (<c>--oauth basic</c> over HTTPS). There is deliberately no connection-string branch, and the
    /// BannedApi analyzer makes the connection-string constructors a build error.
    /// A missing endpoint must not throw here: these are eagerly constructed singletons, so a throw would kill the host
    /// during <c>Build()</c> and defeat the degraded-mode contract. An unreachable placeholder is bound instead and the
    /// storage health check reports the real problem.
    /// </summary>
    public static IServiceCollection AddPoStorage(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        TokenCredential credential = new DefaultAzureCredential();

        // Azurite rejects the audience Azure.Data.Tables asks for; pin the storage scope in that one configuration only.
        if (environment.IsDevelopment() && configuration.GetValue<bool>($"{Flags.Section}:{Flags.UseAzurite}"))
        {
            credential = new AzuriteStorageScopedCredential(credential);
        }

        services.AddSingleton(new TableServiceClient(EndpointOrPlaceholder(configuration, ConfigKeys.Storage.TableEndpoint, "table"), credential));
        services.AddSingleton(new BlobServiceClient(EndpointOrPlaceholder(configuration, ConfigKeys.Storage.BlobEndpoint, "blob"), credential));
        services.AddSingleton(StorageContainers.From(configuration));
        services.AddSingleton<IAudioBlobStore, AudioBlobStore>();
        services.AddHealthChecks().AddCheck<StorageHealthCheck>("storage", tags: [HealthEndpoints.ReadyTag]);
        return services;
    }

    /// <summary>A well-formed URI keeps client construction total; misconfiguration surfaces as a degraded app, not a dead host.</summary>
    public static Uri EndpointOrPlaceholder(IConfiguration configuration, string key, string service)
    {
        var configured = configuration[key];
        return !string.IsNullOrWhiteSpace(configured) && Uri.TryCreate(configured, UriKind.Absolute, out var endpoint)
            ? endpoint
            : new Uri($"https://unconfigured.{service}.core.windows.net/");
    }
}
