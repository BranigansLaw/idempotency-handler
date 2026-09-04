namespace TyFi.Idempotency;

/// <summary>
/// Marks an HTTP-triggered function (or its handler method) as idempotent. When present,
/// the idempotency middleware honours the request's idempotency-key header: a completed
/// duplicate is replayed, a request that reuses a key with a different payload is rejected,
/// and a still-running duplicate is reported as in progress.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IdempotentAttribute : Attribute
{
    /// <summary>
    /// The request header carrying the client-chosen idempotency key.
    /// Defaults to <c>Idempotency-Key</c>.
    /// </summary>
    public string HeaderName { get; init; } = "Idempotency-Key";

    /// <summary>
    /// When <see langword="true"/>, a request that omits the idempotency key is rejected
    /// with <c>400 Bad Request</c>. When <see langword="false"/> (the default) the key is
    /// optional-but-honoured: a request without a key runs normally with no idempotency.
    /// </summary>
    public bool KeyRequired { get; init; }
}
