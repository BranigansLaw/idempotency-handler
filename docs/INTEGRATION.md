# Integration notes

You implement two small seams and register a store. This guide shows both consumer repos.

## The two seams you implement

### 1. Scope provider — who is this request for?

Identity is always `(scope, key)` + request hash. The **scope** isolates one caller's keys
from another's. The package makes no assumption about your auth library — you provide the
scope, typically the authenticated bearer subject.

```csharp
using TyFi.Idempotency;

public sealed class BearerScopeProvider : IIdempotencyScopeProvider
{
    private readonly IMyTokenValidator _tokens;

    public BearerScopeProvider(IMyTokenValidator tokens) => _tokens = tokens;

    public async Task<string?> GetScopeAsync(IIdempotencyExchange exchange, CancellationToken ct)
    {
        string? authorization = exchange.GetHeaderValue("Authorization");
        MyPrincipal? principal = await _tokens.ValidateAsync(authorization, ct);

        // Return null for an unauthenticated request: the executor then runs the function
        // without idempotency, so your function's own auth (401/403) still applies.
        return principal?.Subject;
    }
}
```

### 2. Unit of work — only for the transactional (Postgres) tier

The strong tier commits the idempotency ledger row **in the same transaction** as your
command. Provide an ambient session, registered **scoped**, that is resolved as both
`IIdempotencyDbSession` (connection + transaction) and `IIdempotencyUnitOfWork` (begin /
commit / rollback). The best-effort cache tier uses a built-in `NoOpIdempotencyUnitOfWork`
and needs none of this.

---

## Consumer A — `therapy-scheduling-manager` (ASP.NET Core model + PostgreSQL, strong tier)

This app already has an ambient `IDbSession` (`NpgsqlDbSession`) that owns the request's
Npgsql connection and transaction. Adapt it to the package seams and delete the in-repo
prototype.

**Install**

```bash
dotnet add apps/api/src/TherapyScheduling.Functions package TyFi.Idempotency.Functions.AspNetCore
dotnet add apps/api/src/TherapyScheduling.Infrastructure package TyFi.Idempotency.Postgres
```

**Adapt the existing session** — implement the package interface on the existing type; the
members already match:

```csharp
using TyFi.Idempotency;
using TyFi.Idempotency.Postgres;

// IIdempotencyDbSession : IIdempotencyUnitOfWork already exposes Begin/Commit/Rollback +
// Connection + Transaction — the same shape as the existing IDbSession.
public sealed class NpgsqlDbSession : IDbSession, IIdempotencyDbSession, IAsyncDisposable
{
    // ...unchanged: BeginAsync / CommitAsync / RollbackAsync / Connection / Transaction...
}
```

**Register** in `Program.cs` (replaces `AddIdempotencyMiddlewareServices()` and the manual
`UseWhen`):

```csharp
builder.ConfigureFunctionsWebApplication();
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);

builder.UseIdempotency();                       // scopes middleware to [Idempotent] functions
builder.Services.AddNpgsqlIdempotencyStore();   // IIdempotencyStore -> NpgsqlIdempotencyStore

// Map the app's Auth0 authenticator to the scope seam.
builder.Services.AddScoped<IIdempotencyScopeProvider, Auth0ScopeProvider>();

// The scoped session already registered as IDbSession — also expose the package seams.
builder.Services.AddScoped<IIdempotencyDbSession>(sp => (NpgsqlDbSession)sp.GetRequiredService<IDbSession>());
builder.Services.AddScoped<IIdempotencyUnitOfWork>(sp => (NpgsqlDbSession)sp.GetRequiredService<IDbSession>());

builder.Build().Run();
```

Where `Auth0ScopeProvider` wraps the existing `IBearerTokenAuthenticator`:

```csharp
public sealed class Auth0ScopeProvider(IBearerTokenAuthenticator authenticator) : IIdempotencyScopeProvider
{
    public async Task<string?> GetScopeAsync(IIdempotencyExchange exchange, CancellationToken ct)
    {
        var result = await authenticator.AuthenticateAsync(exchange.GetHeaderValue("Authorization"), ct);
        return result.IsAuthenticated ? result.Subject : null;
    }
}
```

**Schema** — keep the existing `Script0005_IdempotencyKeys.sql`; it matches the package's
`idempotency_keys` table exactly. Then delete the in-repo prototype under
`TherapyScheduling.Functions/Idempotency/`, the `Application/Operations/Idempotency/` store
interfaces, and `Infrastructure/Idempotency/NpgsqlIdempotencyStore.cs`, replacing them with
the package. The two prototype test suites map directly onto the package's own tests.

---

## Consumer B — `TheHubuzz.Api` (built-in worker model + Azure Table Storage, best-effort tier)

Hubuzz keeps its `IDistributedCache` (Azure Table Storage) backend; it just gains scope, a
request hash, an atomic-ish claim, and in-progress handling.

**Install**

```bash
dotnet add TheHubuzz.Api package TyFi.Idempotency.Functions.Worker
dotnet add TheHubuzz.Api package TyFi.Idempotency.DistributedCache
```

**Register** in `Program.cs` (replaces the old `ConfigureIdempotentEndpoints(functionNames)`
and the hand-written middleware):

```csharp
var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults((context, workerApp) =>
    {
        workerApp.UseIdempotency();     // scopes middleware to [Idempotent] functions
    })
    .ConfigureServices(services =>
    {
        // Keep the existing Azure Table Storage distributed cache.
        services.AddDistributedTableStorageCache(/* existing options */);

        services.AddDistributedCacheIdempotencyStore(options =>
        {
            options.CompletedExpiration = TimeSpan.FromHours(12);   // matches the old 12h TTL
        });

        // Map the transaction id to the scope seam (or a real caller identity if available).
        services.AddScoped<IIdempotencyScopeProvider, TransactionScopeProvider>();
    })
    .Build();
```

**Move the key onto the attribute + header.** The old code required a `TransactionId`
header; express that with the attribute and derive scope from your caller identity:

```csharp
[Function("CreatePost")]
[Idempotent(HeaderName = "TransactionId", KeyRequired = true)]
public Task<HttpResponseData> CreatePost(
    [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req, FunctionContext ctx)
{
    // Runs once per (scope, TransactionId, body); duplicates replay the stored response.
}
```

```csharp
public sealed class TransactionScopeProvider : IIdempotencyScopeProvider
{
    // If Hubuzz has an authenticated user, return its id here instead for per-user isolation.
    public Task<string?> GetScopeAsync(IIdempotencyExchange exchange, CancellationToken ct)
        => Task.FromResult<string?>("hubuzz");
}
```

**What you gain over the prototype:** the key is now bound to a request hash (a reused id
with a different body is rejected with `409` instead of silently replaying the wrong
response), an in-flight duplicate returns `409` + `Retry-After` instead of racing, and the
identity is scoped. **What to keep in mind:** this is still the best-effort tier —
`IDistributedCache` cannot make the claim atomic or couple it to a business transaction. If
a Hubuzz endpoint must never run twice, move that endpoint to the Postgres tier.

---

## Choosing a tier at a glance

- **Must never double-execute (payments, orders, bookings):** `TyFi.Idempotency.Postgres`.
- **Nice-to-have dedup, cheap and cache-backed:** `TyFi.Idempotency.DistributedCache`.
- You can use both in one app: strong tier for money endpoints, cache tier for the rest.
