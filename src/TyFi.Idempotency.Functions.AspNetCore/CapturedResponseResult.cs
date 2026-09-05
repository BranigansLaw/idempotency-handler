using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;

namespace TyFi.Idempotency.Functions.AspNetCore;

/// <summary>
/// An <see cref="IActionResult"/> that writes a pre-captured status code, body, content type,
/// and headers to the response exactly once. Used to replace the function's original result (so
/// its captured bytes are delivered by the framework) and to deliver replayed or conflict
/// responses, without the middleware writing <c>Response.Body</c> itself.
/// </summary>
internal sealed class CapturedResponseResult : IActionResult
{
    private static readonly IReadOnlyList<KeyValuePair<string, StringValues>> NoHeaders =
        Array.Empty<KeyValuePair<string, StringValues>>();

    private readonly int _statusCode;
    private readonly byte[] _body;
    private readonly string? _contentType;
    private readonly int? _retryAfterSeconds;
    private readonly IReadOnlyList<KeyValuePair<string, StringValues>> _headers;

    public CapturedResponseResult(
        int statusCode,
        byte[] body,
        string? contentType,
        int? retryAfterSeconds,
        IReadOnlyList<KeyValuePair<string, StringValues>>? headers)
    {
        _statusCode = statusCode;
        _body = body ?? throw new ArgumentNullException(nameof(body));
        _contentType = contentType;
        _retryAfterSeconds = retryAfterSeconds;
        _headers = headers ?? NoHeaders;
    }

    /// <inheritdoc />
    public async Task ExecuteResultAsync(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        HttpResponse response = context.HttpContext.Response;

        // The framework executes this after the middleware unwinds; if the real response has
        // already started, setting the status or headers would throw, so only the body remains.
        if (!response.HasStarted)
        {
            response.StatusCode = _statusCode;
            if (_contentType is not null)
            {
                response.ContentType = _contentType;
            }

            // 1xx and 204 responses must not carry Content-Length; Kestrel rejects it.
            if (_statusCode >= StatusCodes.Status200OK &&
                _statusCode != StatusCodes.Status204NoContent)
            {
                response.ContentLength = _body.Length;
            }

            foreach (KeyValuePair<string, StringValues> header in _headers)
            {
                response.Headers[header.Key] = header.Value;
            }

            if (_retryAfterSeconds is int seconds)
            {
                response.Headers.RetryAfter = seconds.ToString();
            }
        }

        await response.Body.WriteAsync(_body, context.HttpContext.RequestAborted);
    }
}

