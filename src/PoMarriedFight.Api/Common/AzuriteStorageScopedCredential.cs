using Azure.Core;

namespace PoMarriedFight.Api.Common;

/// <summary>
/// Development-only credential shim that pins every storage token request to the Azure Storage scope, so the local
/// Azurite emulator accepts it.
/// </summary>
/// <remarks>
/// <para>
/// Azurite's <c>--oauth basic</c> mode validates the token's <c>aud</c> claim against a fixed whitelist. For Table it
/// accepts <c>https://storage.azure.com</c> and one first-party GUID — but <c>Azure.Data.Tables</c> asks for the Cosmos
/// Table resource instead (<c>aud=a232010e-…</c>), which is not on that list. Every table call then fails 403
/// "Invalid token audience", while blob calls succeed, because the Blob SDK asks for the storage scope directly.
/// </para>
/// <para>
/// Deliberately NOT applied in Production: the shim exists for Azurite, so it is wired only when Azurite is the target.
/// The managed-identity path stays byte-for-byte what it was.
/// </para>
/// </remarks>
public sealed class AzuriteStorageScopedCredential(TokenCredential inner) : TokenCredential
{
    public const string StorageScope = "https://storage.azure.com/.default";

    private static readonly string[] Scopes = [StorageScope];

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        inner.GetToken(Rewrite(requestContext), cancellationToken);

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        inner.GetTokenAsync(Rewrite(requestContext), cancellationToken);

    /// <summary>Swaps the scopes but carries the rest of the context through, so credential logs stay correlated.</summary>
    public static TokenRequestContext Rewrite(TokenRequestContext requestContext) =>
        new(Scopes, requestContext.ParentRequestId, requestContext.Claims, requestContext.TenantId, requestContext.IsCaeEnabled);
}
