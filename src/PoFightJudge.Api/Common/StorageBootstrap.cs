using System.Collections.Concurrent;
using Azure;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace PoFightJudge.Api.Common;

/// <summary>
/// Runs a storage operation, creating its table or blob container at most once per process, per storage endpoint.
/// </summary>
/// <remarks>
/// Repositories are scoped, so an instance-level flag would re-check on every request. Entries are keyed by endpoint
/// URI as well as name so a test that points a fresh Azurite at the same table name still gets its own creation. A
/// first-call race is harmless (both callers issue the same idempotent create). When storage reports the table or
/// container is gone (a wiped Azurite volume, a manual delete) the entry is dropped, the create re-issued and the
/// operation retried once — the self-healing a per-call "create if not exists" used to give for free.
/// </remarks>
public static class StorageBootstrap
{
    private static readonly ConcurrentDictionary<string, bool> Created = new(StringComparer.Ordinal);

    public static Task<T> WithTableAsync<T>(TableServiceClient client, string tableName, Func<CancellationToken, Task<T>> operation, CancellationToken ct) =>
        RunAsync(
            key: $"table|{client.Uri}|{tableName}",
            create: token => client.CreateTableIfNotExistsAsync(tableName, token),
            operation,
            missingErrorCode: "TableNotFound",
            ct);

    public static Task WithTableAsync(TableServiceClient client, string tableName, Func<CancellationToken, Task> operation, CancellationToken ct) =>
        WithTableAsync(client, tableName, Unit(operation), ct);

    public static Task<T> WithContainerAsync<T>(BlobContainerClient container, Func<CancellationToken, Task<T>> operation, CancellationToken ct) =>
        RunAsync(
            key: $"container|{container.Uri}",
            create: token => container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: token),
            operation,
            missingErrorCode: "ContainerNotFound",
            ct);

    public static Task WithContainerAsync(BlobContainerClient container, Func<CancellationToken, Task> operation, CancellationToken ct) =>
        WithContainerAsync(container, Unit(operation), ct);

    /// <summary>Test hook: forget every cached create (a fresh emulator on the same endpoint).</summary>
    public static void Reset() => Created.Clear();

    private static Func<CancellationToken, Task<object?>> Unit(Func<CancellationToken, Task> operation) =>
        async token =>
        {
            await operation(token);
            return null;
        };

    private static async Task<T> RunAsync<T>(string key, Func<CancellationToken, Task> create, Func<CancellationToken, Task<T>> operation, string missingErrorCode, CancellationToken ct)
    {
        await EnsureAsync(key, create, ct);
        try
        {
            return await operation(ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 404 && string.Equals(ex.ErrorCode, missingErrorCode, StringComparison.Ordinal))
        {
            // Deleted after we cached the create. Forget it, recreate, run once more. Only the "container/table is
            // gone" code qualifies — a 404 for a missing row or blob must not trigger a pointless recreate + retry.
            Created.TryRemove(key, out _);
            await EnsureAsync(key, create, ct);
            return await operation(ct);
        }
    }

    private static async Task EnsureAsync(string key, Func<CancellationToken, Task> create, CancellationToken ct)
    {
        if (Created.ContainsKey(key))
        {
            return;
        }

        try
        {
            await create(ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 409 && ex.ErrorCode is "ContainerAlreadyExists" or "TableAlreadyExists")
        {
            // Two requests observed it as missing at once; whichever lost the race gets a 409. It exists either way.
        }

        Created[key] = true;
    }
}
