using Microsoft.AspNetCore.Http;

namespace TyFi.Idempotency.Functions.AspNetCore;

/// <summary>
/// <see cref="IIdempotencyExchange"/> over the ASP.NET Core integration model. The
/// response is captured by buffering <see cref="HttpResponse.Body"/> while the function
/// runs, then flushed to the client so it is delivered whether or not the ledger commits.
/// </summary>
internal sealed class HttpContextIdempotencyExchange : IIdempotencyExchange
{
    private readonly HttpContext _http;

    public HttpContextIdempotencyExchange(HttpContext http)
    {
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
        Stream originalBody = _http.Response.Body;
        using var buffer = new MemoryStream();
        _http.Response.Body = buffer;
        try
        {
            await next();
        }
        finally
        {
            _http.Response.Body = originalBody;
        }

        byte[] body = buffer.ToArray();
        await originalBody.WriteAsync(body, cancellationToken);
        return new IdempotencyCapturedResponse(_http.Response.StatusCode, body);
    }

    public async Task ShortCircuitAsync(
        int statusCode,
        byte[] body,
        string contentType,
        int? retryAfterSeconds,
        CancellationToken cancellationToken)
    {
        _http.Response.StatusCode = statusCode;
        _http.Response.ContentType = contentType;
        if (retryAfterSeconds is int seconds)
        {
            _http.Response.Headers.RetryAfter = seconds.ToString();
        }

        await _http.Response.Body.WriteAsync(body, cancellationToken);
    }
}
