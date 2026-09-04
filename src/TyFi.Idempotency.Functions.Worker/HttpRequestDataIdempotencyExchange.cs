using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace TyFi.Idempotency.Functions.Worker;

/// <summary>
/// <see cref="IIdempotencyExchange"/> over the built-in worker HTTP model. The response is
/// captured from the invocation result (an <see cref="HttpResponseData"/>); a short-circuit
/// creates a new response and assigns it as the invocation result.
/// </summary>
internal sealed class HttpRequestDataIdempotencyExchange : IIdempotencyExchange
{
    private readonly FunctionContext _context;
    private readonly HttpRequestData _request;

    public HttpRequestDataIdempotencyExchange(FunctionContext context, HttpRequestData request)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _request = request ?? throw new ArgumentNullException(nameof(request));
    }

    public string Method => _request.Method;

    public string PathAndQuery => _request.Url.PathAndQuery;

    public string? GetHeaderValue(string name) =>
        _request.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    public async Task<byte[]> ReadRequestBodyAsync(CancellationToken cancellationToken)
    {
        Stream body = _request.Body;
        if (body.CanSeek)
        {
            body.Position = 0;
        }

        using var buffer = new MemoryStream();
        await body.CopyToAsync(buffer, cancellationToken);
        if (body.CanSeek)
        {
            body.Position = 0;
        }

        return buffer.ToArray();
    }

    public async Task<IdempotencyCapturedResponse> InvokeAndCaptureAsync(
        Func<Task> next,
        CancellationToken cancellationToken)
    {
        await next();

        HttpResponseData? response = _context.GetHttpResponseData();
        if (response is null)
        {
            return new IdempotencyCapturedResponse((int)HttpStatusCode.OK, []);
        }

        byte[] body = await ReadBodyAsync(response.Body, cancellationToken);
        return new IdempotencyCapturedResponse((int)response.StatusCode, body);
    }

    public async Task ShortCircuitAsync(
        int statusCode,
        byte[] body,
        string contentType,
        int? retryAfterSeconds,
        CancellationToken cancellationToken)
    {
        HttpResponseData response = _request.CreateResponse((HttpStatusCode)statusCode);
        response.Headers.Add("Content-Type", contentType);
        if (retryAfterSeconds is int seconds)
        {
            response.Headers.Add("Retry-After", seconds.ToString());
        }

        await response.Body.WriteAsync(body, cancellationToken);
        _context.GetInvocationResult().Value = response;
    }

    private static async Task<byte[]> ReadBodyAsync(Stream body, CancellationToken cancellationToken)
    {
        if (body.CanSeek)
        {
            body.Position = 0;
        }

        using var buffer = new MemoryStream();
        await body.CopyToAsync(buffer, cancellationToken);
        if (body.CanSeek)
        {
            body.Position = 0;
        }

        return buffer.ToArray();
    }
}
