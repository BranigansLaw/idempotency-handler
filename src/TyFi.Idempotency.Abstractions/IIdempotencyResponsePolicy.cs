namespace TyFi.Idempotency;

/// <summary>
/// Decides which responses are cacheable — that is, stored and replayed. Only cacheable
/// responses commit the ledger; every other response rolls the claim back so a corrected
/// retry proceeds and authorization decisions (for example <c>403</c>) are never cached.
/// </summary>
public interface IIdempotencyResponsePolicy
{
    /// <summary>
    /// Returns whether a response with this status code should be stored and replayed.
    /// </summary>
    bool IsCacheable(int statusCode);
}

/// <summary>
/// Default policy: only <c>2xx</c> responses are cacheable.
/// </summary>
public sealed class SuccessStatusIdempotencyResponsePolicy : IIdempotencyResponsePolicy
{
    /// <inheritdoc />
    public bool IsCacheable(int statusCode) => statusCode is >= 200 and < 300;
}
