using System.Security.Cryptography;
using Azure;
using Azure.Data.Tables;
using Microsoft.AspNetCore.WebUtilities;
using PoFightJudge.Api.Common;
using PoFightJudge.Api.Features.Storage;
using PoFightJudge.Shared.Identifiers;

namespace PoFightJudge.Api.Features.Records;

/// <summary>Which match a share token points at, and whose it is. The owner is carried so revoking can check it.</summary>
public sealed record ShareTarget(string UserId, MatchId MatchId);

/// <summary>
/// The reverse index for shared rulings. A token is the only thing a reader has, so it is the partition key and
/// resolving one is a point read; the match row keeps the same token so the owner's page knows it is shared without
/// scanning the index.
///
/// Tokens are 128 bits of cryptographic randomness in base64url. A ruling names two people and quotes a judge on
/// them, so the token is the whole of the access control: it has to be unguessable rather than merely unique.
/// </summary>
public interface IShareRepository
{
    /// <summary>Shares a match, or returns the token it is already shared under. Sharing twice is not two links.</summary>
    Task<string> ShareAsync(string userId, MatchId matchId, string? existingToken, CancellationToken ct = default);

    /// <summary>Takes a share back. Safe to call when there was nothing shared.</summary>
    Task RevokeAsync(string token, CancellationToken ct = default);

    /// <summary>Who a token points at, or null when it was never issued or has been taken back.</summary>
    Task<ShareTarget?> ResolveAsync(string token, CancellationToken ct = default);
}

public sealed class ShareRepository(TableServiceClient tables) : IShareRepository
{
    /// <summary>One row per token, so the row key is a constant rather than a second thing to know.</summary>
    private const string Row = "share";

    public static string NewToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(16));

    public Task<string> ShareAsync(string userId, MatchId matchId, string? existingToken, CancellationToken ct = default)
    {
        if (existingToken is { Length: > 0 })
        {
            return Task.FromResult(existingToken);
        }

        var token = NewToken();
        return StorageBootstrap.WithTableAsync(tables, TableNames.Shares, async cancellation =>
        {
            await Table().UpsertEntityAsync(
                new ShareEntity { PartitionKey = token, RowKey = Row, UserId = userId, MatchId = matchId.Value },
                TableUpdateMode.Replace,
                cancellation);
            return token;
        }, ct);
    }

    public Task RevokeAsync(string token, CancellationToken ct = default) =>
        StorageBootstrap.WithTableAsync(tables, TableNames.Shares, async cancellation =>
        {
            try
            {
                await Table().DeleteEntityAsync(token, Row, cancellationToken: cancellation);
            }
            catch (RequestFailedException failure) when (failure.Status == StatusCodes.Status404NotFound)
            {
                // Already gone. Revoking twice is not an error; the link is dead either way.
            }
        }, ct);

    public Task<ShareTarget?> ResolveAsync(string token, CancellationToken ct = default) =>
        StorageBootstrap.WithTableAsync<ShareTarget?>(tables, TableNames.Shares, async cancellation =>
        {
            var response = await Table().GetEntityIfExistsAsync<ShareEntity>(token, Row, cancellationToken: cancellation);
            return response.HasValue && response.Value is { } row
                ? new ShareTarget(row.UserId, MatchId.From(row.MatchId))
                : null;
        }, ct);

    private TableClient Table() => tables.GetTableClient(TableNames.Shares);
}
