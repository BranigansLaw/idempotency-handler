namespace TyFi.Idempotency;

/// <summary>
/// Records and replays the results of externally retryable commands, keyed by an
/// idempotency key within a scope and bound to a request hash. Implementations decide
/// their own atomicity guarantees:
/// <list type="bullet">
///   <item>the transactional tier (e.g. PostgreSQL) enlists in the host's business
///   transaction so the ledger row commits with the command's side effect; and</item>
///   <item>the best-effort tier (e.g. a distributed cache) offers replay without the
///   strong atomic-claim or transactional-coupling guarantees.</item>
/// </list>
/// Identity is always <c>(scope, key)</c> plus the request hash; never key on the
/// client key alone.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Attempts to claim the key for this request. Returns whether the caller won the
    /// claim, must replay a completed response, is reusing the key with a different
    /// payload, or is racing an in-flight request.
    /// </summary>
    Task<IdempotencyClaimResult> TryClaimAsync(
        string scope,
        string key,
        byte[] requestHash,
        CancellationToken cancellationToken);

    /// <summary>
    /// Stores the response for a claimed key so later duplicates can replay it.
    /// </summary>
    Task CompleteAsync(
        string scope,
        string key,
        int statusCode,
        byte[] responseBody,
        CancellationToken cancellationToken);
}
