namespace TyFi.Idempotency;

/// <summary>
/// The outcome of attempting to claim an idempotency key.
/// </summary>
public enum IdempotencyClaimOutcome
{
    /// <summary>
    /// This request won the claim and should execute the command.
    /// </summary>
    Claimed,

    /// <summary>
    /// The key was already used by a completed request with the same payload; replay its response.
    /// </summary>
    AlreadyCompleted,

    /// <summary>
    /// The key was already used with a different payload; reject the request.
    /// </summary>
    RequestMismatch,

    /// <summary>
    /// An earlier request with this key is still in flight; retry later.
    /// </summary>
    InProgress,
}

/// <summary>
/// The result of a claim attempt.
/// </summary>
/// <param name="Outcome">Whether the claim was won, completed, mismatched, or in progress.</param>
/// <param name="StatusCode">The stored response status code when the outcome is completed; otherwise null.</param>
/// <param name="ResponseBody">The stored response body when the outcome is completed; otherwise null.</param>
public sealed record IdempotencyClaimResult(
    IdempotencyClaimOutcome Outcome,
    int? StatusCode,
    byte[]? ResponseBody)
{
    /// <summary>
    /// Creates a result for a request that won the claim.
    /// </summary>
    public static IdempotencyClaimResult Claimed() =>
        new(IdempotencyClaimOutcome.Claimed, null, null);

    /// <summary>
    /// Creates a result carrying a completed request's stored response for replay.
    /// </summary>
    public static IdempotencyClaimResult Completed(int statusCode, byte[] responseBody) =>
        new(IdempotencyClaimOutcome.AlreadyCompleted, statusCode, responseBody);

    /// <summary>
    /// Creates a result for a key reused with a different payload.
    /// </summary>
    public static IdempotencyClaimResult RequestMismatch() =>
        new(IdempotencyClaimOutcome.RequestMismatch, null, null);

    /// <summary>
    /// Creates a result for a key whose earlier request is still running.
    /// </summary>
    public static IdempotencyClaimResult InProgress() =>
        new(IdempotencyClaimOutcome.InProgress, null, null);
}
