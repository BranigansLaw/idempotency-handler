namespace TyFi.Idempotency;

/// <summary>
/// Derives the idempotency scope for a request. Identity is always <c>(scope, key)</c>
/// plus the request hash, so the scope isolates one caller's keys from another's.
/// </summary>
/// <remarks>
/// The package does not assume an authentication library. A typical implementation
/// returns the authenticated bearer subject; a request that cannot be scoped (for
/// example an unauthenticated one) returns <see langword="null"/>, and the executor then
/// runs the function without idempotency so the function's own authorization applies.
/// </remarks>
public interface IIdempotencyScopeProvider
{
    /// <summary>
    /// Returns the scope for the request, or <see langword="null"/> when the request
    /// cannot be scoped and idempotency should not apply.
    /// </summary>
    Task<string?> GetScopeAsync(IIdempotencyExchange exchange, CancellationToken cancellationToken);
}
