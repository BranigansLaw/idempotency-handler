namespace TyFi.Idempotency;

/// <summary>
/// A response captured from the wrapped function: the status code and the full body.
/// </summary>
/// <param name="StatusCode">The HTTP status code the function produced.</param>
/// <param name="Body">The response body bytes the function produced.</param>
public readonly record struct IdempotencyCapturedResponse(int StatusCode, byte[] Body);

/// <summary>
/// An HTTP-model-agnostic view of a single request/response exchange. Two thin adapters
/// implement it — one over the ASP.NET Core integration model (<c>HttpContext</c>) and
/// one over the built-in worker model (<c>HttpRequestData</c>/<c>HttpResponseData</c>) —
/// so the executor never depends on a specific HTTP type.
/// </summary>
public interface IIdempotencyExchange
{
    /// <summary>
    /// The request method (for example <c>POST</c>).
    /// </summary>
    string Method { get; }

    /// <summary>
    /// The request path together with its query string.
    /// </summary>
    string PathAndQuery { get; }

    /// <summary>
    /// Returns the first value of the named request header, or <see langword="null"/>
    /// when it is absent.
    /// </summary>
    string? GetHeaderValue(string name);

    /// <summary>
    /// Reads the full request body, leaving it re-readable by the function.
    /// </summary>
    Task<byte[]> ReadRequestBodyAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Invokes the wrapped function, captures its status code and body, and ensures that
    /// response is what the client receives. The executor uses the captured status only
    /// to decide whether to store and commit; the response is delivered either way.
    /// </summary>
    Task<IdempotencyCapturedResponse> InvokeAndCaptureAsync(
        Func<Task> next,
        CancellationToken cancellationToken);

    /// <summary>
    /// Writes a response the function did not produce — a replayed stored response, or a
    /// conflict — short-circuiting the invocation.
    /// </summary>
    Task ShortCircuitAsync(
        int statusCode,
        byte[] body,
        string contentType,
        int? retryAfterSeconds,
        CancellationToken cancellationToken);
}
