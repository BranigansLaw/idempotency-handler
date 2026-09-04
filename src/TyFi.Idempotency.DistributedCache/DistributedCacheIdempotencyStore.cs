using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace TyFi.Idempotency.DistributedCache;

/// <summary>
/// Best-effort idempotency store over an <see cref="IDistributedCache"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the weaker of the two correctness tiers. <see cref="IDistributedCache"/> cannot
/// express an atomic claim or couple the ledger write to a business transaction, so:
/// </para>
/// <list type="bullet">
///   <item>the claim has a small race window — two simultaneous first requests can both
///   win — because "read then write" is not atomic; and</item>
///   <item>the stored response is not transactional with the command's side effect.</item>
/// </list>
/// <para>
/// Use it for the best-effort replay tier only. For a strong guarantee use the
/// transactional tier (for example <c>TyFi.Idempotency.Postgres</c>).
/// </para>
/// </remarks>
public sealed class DistributedCacheIdempotencyStore : IIdempotencyStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IDistributedCache _cache;
    private readonly DistributedCacheIdempotencyOptions _options;

    /// <summary>
    /// Creates the store over a distributed cache and its options.
    /// </summary>
    public DistributedCacheIdempotencyStore(
        IDistributedCache cache,
        IOptions<DistributedCacheIdempotencyOptions> options)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
    }

    /// <inheritdoc />
    public async Task<IdempotencyClaimResult> TryClaimAsync(
        string scope,
        string key,
        byte[] requestHash,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(requestHash);

        string cacheKey = BuildKey(scope, key);
        byte[]? raw = await _cache.GetAsync(cacheKey, cancellationToken);

        if (raw is null)
        {
            var marker = new CacheEntry(requestHash, Completed: false, StatusCode: null, Body: null);
            await _cache.SetAsync(cacheKey, Serialize(marker), InProgressEntryOptions(), cancellationToken);
            return IdempotencyClaimResult.Claimed();
        }

        CacheEntry entry = Deserialize(raw);
        if (!entry.RequestHash.AsSpan().SequenceEqual(requestHash))
        {
            return IdempotencyClaimResult.RequestMismatch();
        }

        if (!entry.Completed)
        {
            return IdempotencyClaimResult.InProgress();
        }

        return IdempotencyClaimResult.Completed(entry.StatusCode!.Value, entry.Body!);
    }

    /// <inheritdoc />
    public async Task CompleteAsync(
        string scope,
        string key,
        int statusCode,
        byte[] responseBody,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(responseBody);

        string cacheKey = BuildKey(scope, key);

        // Preserve the request hash written by the claim so a later duplicate still
        // validates against the original payload.
        byte[]? marker = await _cache.GetAsync(cacheKey, cancellationToken);
        byte[] requestHash = marker is null ? [] : Deserialize(marker).RequestHash;

        var completed = new CacheEntry(requestHash, Completed: true, statusCode, responseBody);
        await _cache.SetAsync(
            cacheKey, Serialize(completed), CompletedEntryOptions(), cancellationToken);
    }

    private string BuildKey(string scope, string key) => $"{_options.KeyPrefix}{scope}:{key}";

    private DistributedCacheEntryOptions InProgressEntryOptions() =>
        new() { AbsoluteExpirationRelativeToNow = _options.InProgressExpiration };

    private DistributedCacheEntryOptions CompletedEntryOptions() =>
        new() { AbsoluteExpirationRelativeToNow = _options.CompletedExpiration };

    private static byte[] Serialize(CacheEntry entry) =>
        JsonSerializer.SerializeToUtf8Bytes(entry, SerializerOptions);

    private static CacheEntry Deserialize(byte[] raw) =>
        JsonSerializer.Deserialize<CacheEntry>(raw, SerializerOptions)
            ?? throw new InvalidOperationException("Failed to deserialize the idempotency cache entry.");

    private sealed record CacheEntry(
        byte[] RequestHash,
        bool Completed,
        int? StatusCode,
        byte[]? Body);
}
