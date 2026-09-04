using System.Text;

namespace TyFi.Idempotency.Tests;

/// <summary>
/// A fake <see cref="IIdempotencyExchange"/> that records short-circuits and lets a test
/// supply the wrapped function's captured response.
/// </summary>
internal sealed class FakeExchange : IIdempotencyExchange
{
    private readonly Dictionary<string, string> _headers = new(StringComparer.OrdinalIgnoreCase);

    public FakeExchange(string? authorization, string? idempotencyKey, string body = "{\"name\":\"a\"}")
    {
        if (authorization is not null)
        {
            _headers["Authorization"] = authorization;
        }

        if (idempotencyKey is not null)
        {
            _headers["Idempotency-Key"] = idempotencyKey;
        }

        RequestBody = Encoding.UTF8.GetBytes(body);
    }

    public string Method { get; set; } = "POST";

    public string PathAndQuery { get; set; } = "/api/practice/services";

    public byte[] RequestBody { get; }

    public bool Invoked { get; private set; }

    public int? ShortCircuitStatus { get; private set; }

    public string? ShortCircuitBody { get; private set; }

    public int? ShortCircuitRetryAfter { get; private set; }

    // Set by the test's next() delegate to simulate the function's response.
    public int NextStatusCode { get; set; } = 200;

    public byte[] NextBody { get; set; } = [];

    public string? GetHeaderValue(string name) =>
        _headers.TryGetValue(name, out string? value) ? value : null;

    public Task<byte[]> ReadRequestBodyAsync(CancellationToken cancellationToken) =>
        Task.FromResult(RequestBody);

    public async Task<IdempotencyCapturedResponse> InvokeAndCaptureAsync(
        Func<Task> next,
        CancellationToken cancellationToken)
    {
        await next();
        Invoked = true;
        return new IdempotencyCapturedResponse(NextStatusCode, NextBody);
    }

    public Task ShortCircuitAsync(
        int statusCode,
        byte[] body,
        string contentType,
        int? retryAfterSeconds,
        CancellationToken cancellationToken)
    {
        ShortCircuitStatus = statusCode;
        ShortCircuitBody = Encoding.UTF8.GetString(body);
        ShortCircuitRetryAfter = retryAfterSeconds;
        return Task.CompletedTask;
    }
}

/// <summary>
/// A scope provider that returns the bearer subject when an Authorization header is
/// present, mirroring how a real host scopes idempotency to the caller.
/// </summary>
internal sealed class FakeScopeProvider : IIdempotencyScopeProvider
{
    private readonly string _subject;

    public FakeScopeProvider(string subject = "auth0|jane") => _subject = subject;

    public Task<string?> GetScopeAsync(IIdempotencyExchange exchange, CancellationToken cancellationToken) =>
        Task.FromResult(exchange.GetHeaderValue("Authorization") is null ? null : _subject);
}

internal sealed class FakeUnitOfWork : IIdempotencyUnitOfWork
{
    public int Begins { get; private set; }

    public int Commits { get; private set; }

    public int Rollbacks { get; private set; }

    public bool IsActive { get; private set; }

    public Task BeginAsync(CancellationToken cancellationToken)
    {
        Begins++;
        IsActive = true;
        return Task.CompletedTask;
    }

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        Commits++;
        IsActive = false;
        return Task.CompletedTask;
    }

    public Task RollbackAsync(CancellationToken cancellationToken)
    {
        Rollbacks++;
        IsActive = false;
        return Task.CompletedTask;
    }
}

internal sealed class FakeIdempotencyStore : IIdempotencyStore
{
    private readonly IdempotencyClaimResult _claim;

    public FakeIdempotencyStore(IdempotencyClaimResult claim) => _claim = claim;

    public bool Completed { get; private set; }

    public int? CompletedStatus { get; private set; }

    public Task<IdempotencyClaimResult> TryClaimAsync(
        string scope,
        string key,
        byte[] requestHash,
        CancellationToken cancellationToken) => Task.FromResult(_claim);

    public Task CompleteAsync(
        string scope,
        string key,
        int statusCode,
        byte[] responseBody,
        CancellationToken cancellationToken)
    {
        Completed = true;
        CompletedStatus = statusCode;
        return Task.CompletedTask;
    }
}
