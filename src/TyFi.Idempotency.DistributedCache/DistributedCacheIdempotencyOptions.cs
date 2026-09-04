namespace TyFi.Idempotency.DistributedCache;

/// <summary>
/// Options for the best-effort <see cref="DistributedCacheIdempotencyStore"/>.
/// </summary>
public sealed class DistributedCacheIdempotencyOptions
{
    /// <summary>
    /// Prefix for cache keys. Defaults to <c>idem:</c>.
    /// </summary>
    public string KeyPrefix { get; set; } = "idem:";

    /// <summary>
    /// How long an in-progress claim marker lives before it is treated as abandoned and a
    /// retry may re-claim the key. Defaults to 5 minutes.
    /// </summary>
    public TimeSpan InProgressExpiration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long a completed response is retained for replay. Defaults to 24 hours.
    /// </summary>
    public TimeSpan CompletedExpiration { get; set; } = TimeSpan.FromHours(24);
}
