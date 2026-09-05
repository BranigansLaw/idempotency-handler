using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;

namespace TyFi.Idempotency.Functions.AspNetCore;

/// <summary>
/// <see cref="IIdempotencyExchange"/> over the ASP.NET Core integration model. Under this
/// integration the function's result is executed to the response by the framework's proxying
/// middleware <b>after</b> this middleware unwinds, so the response is captured from and
/// replaced on the invocation result, never by writing <see cref="HttpResponse.Body"/>
/// directly.
/// </summary>
/// <remarks>
/// Only <see cref="IActionResult"/> and <see cref="IResult"/> results are supported. Other
/// shapes the host can proxy (raw <c>HttpResponseData</c>, multi-output bindings, or a
/// function that writes <see cref="HttpResponse.Body"/> directly) are rejected rather than
/// silently cached as an empty <c>200</c>, which would poison the idempotency ledger.
/// Note that only the status code and body survive a cross-process replay (see
/// <see cref="IIdempotencyStore"/>); captured response headers and content type are delivered
/// on the first request only.
/// </remarks>
internal sealed class HttpContextIdempotencyExchange : IIdempotencyExchange
{
    private readonly IInvocationResultAccessor _invocationResult;
    private readonly HttpContext _http;

    public HttpContextIdempotencyExchange(IInvocationResultAccessor invocationResult, HttpContext http)
    {
        _invocationResult = invocationResult ?? throw new ArgumentNullException(nameof(invocationResult));
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    public string Method => _http.Request.Method;

    public string PathAndQuery => _http.Request.Path.Value + _http.Request.QueryString.Value;

    public string? GetHeaderValue(string name) =>
        _http.Request.Headers.TryGetValue(name, out var values) ? values.FirstOrDefault() : null;

    public async Task<byte[]> ReadRequestBodyAsync(CancellationToken cancellationToken)
    {
        _http.Request.EnableBuffering();
        _http.Request.Body.Position = 0;
        using var buffer = new MemoryStream();
        await _http.Request.Body.CopyToAsync(buffer, cancellationToken);
        _http.Request.Body.Position = 0;
        return buffer.ToArray();
    }

    public async Task<IdempotencyCapturedResponse> InvokeAndCaptureAsync(
        Func<Task> next,
        CancellationToken cancellationToken)
    {
        await next();

        // Under this integration the framework executes the result after this middleware
        // unwinds, so we cannot read the response from Response.Body here. Render the result to
        // capture status + body + headers, then replace it with a pre-rendered result so the
        // framework delivers exactly those bytes once, with the correct status.
        CapturedResult captured = await RenderAsync(_invocationResult.Value, cancellationToken);

        _invocationResult.Value = new CapturedResponseResult(
            captured.StatusCode, captured.Body, captured.ContentType, retryAfterSeconds: null, captured.Headers);

        return new IdempotencyCapturedResponse(captured.StatusCode, captured.Body);
    }

    public Task ShortCircuitAsync(
        int statusCode,
        byte[] body,
        string contentType,
        int? retryAfterSeconds,
        CancellationToken cancellationToken)
    {
        // Replace the invocation result so the framework delivers this response; writing
        // Response.Body directly here would collide with the framework's result execution.
        _invocationResult.Value = new CapturedResponseResult(
            statusCode, body, contentType, retryAfterSeconds, headers: null);
        return Task.CompletedTask;
    }

    private async Task<CapturedResult> RenderAsync(object? result, CancellationToken cancellationToken)
    {
        // Execute the result against a scratch response to capture its status, body, headers,
        // and content type without touching the real Kestrel response. RequestServices carries
        // the MVC formatters the result needs; the request line and Accept headers are copied
        // so content negotiation and HEAD handling match the real request.
        var scratch = new DefaultHttpContext { RequestServices = _http.RequestServices };
        scratch.RequestAborted = cancellationToken;
        scratch.Request.Method = _http.Request.Method;
        scratch.Request.Path = _http.Request.Path;
        scratch.Request.QueryString = _http.Request.QueryString;
        CopyNegotiationHeaders(_http.Request.Headers, scratch.Request.Headers);

        using var buffer = new MemoryStream();
        scratch.Response.Body = buffer;

        switch (result)
        {
            case IActionResult actionResult:
                await actionResult.ExecuteResultAsync(
                    new ActionContext(scratch, new RouteData(), new ActionDescriptor()));
                break;

            case IResult minimalResult:
                await minimalResult.ExecuteAsync(scratch);
                break;

            default:
                throw new NotSupportedException(
                    $"[Idempotent] supports functions returning {nameof(IActionResult)} or " +
                    $"{nameof(IResult)}; the result was " +
                    $"'{result?.GetType().FullName ?? "null"}'. Return an action result so the " +
                    "response can be captured and replayed.");
        }

        await scratch.Response.CompleteAsync();

        string? contentType = string.IsNullOrEmpty(scratch.Response.ContentType)
            ? null
            : scratch.Response.ContentType;

        return new CapturedResult(
            scratch.Response.StatusCode,
            buffer.ToArray(),
            contentType,
            SnapshotHeaders(scratch.Response.Headers));
    }

    private static void CopyNegotiationHeaders(IHeaderDictionary source, IHeaderDictionary destination)
    {
        foreach (string name in new[] { "Accept", "Accept-Charset", "Accept-Encoding", "Accept-Language" })
        {
            if (source.TryGetValue(name, out StringValues values))
            {
                destination[name] = values;
            }
        }
    }

    private static IReadOnlyList<KeyValuePair<string, StringValues>> SnapshotHeaders(IHeaderDictionary headers)
    {
        var snapshot = new List<KeyValuePair<string, StringValues>>(headers.Count);
        foreach (KeyValuePair<string, StringValues> header in headers)
        {
            // Content-Type is applied via Response.ContentType; length/framing are recomputed.
            if (string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(header.Key, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            snapshot.Add(header);
        }

        return snapshot;
    }

    private readonly record struct CapturedResult(
        int StatusCode,
        byte[] Body,
        string? ContentType,
        IReadOnlyList<KeyValuePair<string, StringValues>> Headers);
}
