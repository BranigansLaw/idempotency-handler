using System.Text;
using System.Text.Json;

namespace TyFi.Idempotency;

/// <summary>
/// Runs the idempotency protocol around an HTTP function invocation: derive the scope,
/// claim the key inside the ambient unit of work, then either replay a stored response,
/// report a conflict, or execute the function and store its response atomically.
/// </summary>
public interface IIdempotencyExecutor
{
    /// <summary>
    /// Executes the protocol. Calls <paramref name="next"/> only when the request wins
    /// the claim (or when idempotency does not apply).
    /// </summary>
    Task ExecuteAsync(
        IIdempotencyExchange exchange,
        IdempotentAttribute options,
        Func<Task> next,
        CancellationToken cancellationToken);
}

/// <summary>
/// Default idempotency protocol, agnostic to the HTTP model. The claim, the function's
/// side effect, and the stored response all run on one <see cref="IIdempotencyUnitOfWork"/>
/// so they commit or roll back together; only a cacheable (by default <c>2xx</c>) response
/// is committed and replayed, so failed and unauthorized requests leave no trace and can
/// be retried.
/// </summary>
public sealed class IdempotencyExecutor : IIdempotencyExecutor
{
    private const string JsonContentType = "application/json";

    private readonly IIdempotencyUnitOfWork _unitOfWork;
    private readonly IIdempotencyStore _store;
    private readonly IIdempotencyScopeProvider _scopeProvider;
    private readonly IIdempotencyRequestHasher _hasher;
    private readonly IIdempotencyResponsePolicy _responsePolicy;

    /// <summary>
    /// Creates the executor over its unit of work, store, scope provider, hasher, and
    /// response policy.
    /// </summary>
    public IdempotencyExecutor(
        IIdempotencyUnitOfWork unitOfWork,
        IIdempotencyStore store,
        IIdempotencyScopeProvider scopeProvider,
        IIdempotencyRequestHasher hasher,
        IIdempotencyResponsePolicy responsePolicy)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _scopeProvider = scopeProvider ?? throw new ArgumentNullException(nameof(scopeProvider));
        _hasher = hasher ?? throw new ArgumentNullException(nameof(hasher));
        _responsePolicy = responsePolicy ?? throw new ArgumentNullException(nameof(responsePolicy));
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(
        IIdempotencyExchange exchange,
        IdempotentAttribute options,
        Func<Task> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exchange);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(next);

        // Scope idempotency to the caller. A request that cannot be scoped (for example
        // an unauthenticated one) is left to the function, which enforces its own auth.
        string? scope = await _scopeProvider.GetScopeAsync(exchange, cancellationToken);
        if (string.IsNullOrWhiteSpace(scope))
        {
            await next();
            return;
        }

        string? key = exchange.GetHeaderValue(options.HeaderName)?.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            if (options.KeyRequired)
            {
                await WriteProblemAsync(
                    exchange,
                    StatusCodes.BadRequest,
                    $"The '{options.HeaderName}' header is required.",
                    retryAfterSeconds: null,
                    cancellationToken);
                return;
            }

            // Optional-but-honoured: no key means run normally, no idempotency.
            await next();
            return;
        }

        byte[] body = await exchange.ReadRequestBodyAsync(cancellationToken);
        byte[] requestHash = _hasher.Hash(scope, exchange.Method, exchange.PathAndQuery, body);

        await _unitOfWork.BeginAsync(cancellationToken);
        try
        {
            IdempotencyClaimResult claim =
                await _store.TryClaimAsync(scope, key, requestHash, cancellationToken);

            switch (claim.Outcome)
            {
                case IdempotencyClaimOutcome.InProgress:
                    await _unitOfWork.RollbackAsync(cancellationToken);
                    await WriteProblemAsync(
                        exchange,
                        StatusCodes.Conflict,
                        "A request with this idempotency key is still being processed. Retry shortly.",
                        retryAfterSeconds: 1,
                        cancellationToken);
                    return;

                case IdempotencyClaimOutcome.RequestMismatch:
                    await _unitOfWork.RollbackAsync(cancellationToken);
                    await WriteProblemAsync(
                        exchange,
                        StatusCodes.Conflict,
                        "This idempotency key was already used for a different request.",
                        retryAfterSeconds: null,
                        cancellationToken);
                    return;

                case IdempotencyClaimOutcome.AlreadyCompleted:
                    await _unitOfWork.RollbackAsync(cancellationToken);
                    await exchange.ShortCircuitAsync(
                        claim.StatusCode!.Value,
                        claim.ResponseBody!,
                        JsonContentType,
                        retryAfterSeconds: null,
                        cancellationToken);
                    return;
            }

            // We won the claim: run the function and capture its response.
            IdempotencyCapturedResponse captured =
                await exchange.InvokeAndCaptureAsync(next, cancellationToken);

            if (_responsePolicy.IsCacheable(captured.StatusCode))
            {
                await _store.CompleteAsync(
                    scope, key, captured.StatusCode, captured.Body, cancellationToken);
                await _unitOfWork.CommitAsync(cancellationToken);
            }
            else
            {
                await _unitOfWork.RollbackAsync(cancellationToken);
            }
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static Task WriteProblemAsync(
        IIdempotencyExchange exchange,
        int statusCode,
        string message,
        int? retryAfterSeconds,
        CancellationToken cancellationToken)
    {
        byte[] payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { message }));
        return exchange.ShortCircuitAsync(
            statusCode, payload, JsonContentType, retryAfterSeconds, cancellationToken);
    }

    private static class StatusCodes
    {
        public const int BadRequest = 400;
        public const int Conflict = 409;
    }
}
