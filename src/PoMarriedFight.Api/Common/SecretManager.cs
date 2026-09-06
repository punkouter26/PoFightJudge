using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Security.KeyVault.Secrets;
using PoMarriedFight.Shared.Configuration;

namespace PoMarriedFight.Api.Common;

/// <summary>
/// Loads only <c>PoMarriedFight--*</c> secrets from the shared vault (<c>kv-poshared</c> also holds the sibling
/// apps' secrets) and maps them under <c>PoMarriedFight:</c>, so <c>PoMarriedFight--AzureAd--ClientId</c> becomes
/// <c>PoMarriedFight:AzureAd:ClientId</c>.
/// </summary>
public sealed class PoMarriedFightSecretManager : KeyVaultSecretManager
{
    public const string Prefix = ConfigKeys.Root + "--";

    public override bool Load(SecretProperties secret) => ShouldLoad(secret.Name);

    public override string GetKey(KeyVaultSecret secret) => MapKey(secret.Name);

    public static bool ShouldLoad(string secretName) => secretName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>Pure mapping used by the override and the tests.</summary>
    public static string MapKey(string secretName)
    {
        var tail = secretName[Prefix.Length..].Replace("--", ConfigurationPath.KeyDelimiter, StringComparison.Ordinal);
        return ConfigurationPath.Combine(ConfigKeys.Root, tail);
    }
}
