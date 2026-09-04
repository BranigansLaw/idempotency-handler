using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.DependencyInjection;

namespace TyFi.Idempotency.Functions.Worker;

/// <summary>
/// Isolated-worker middleware that applies the idempotency protocol to built-in HTTP
/// model functions marked with <see cref="IdempotentAttribute"/>. It wraps the
/// <see cref="HttpRequestData"/> in an exchange and delegates the protocol to the shared
/// <see cref="IIdempotencyExecutor"/>.
/// </summary>
public sealed class IdempotencyWorkerMiddleware : IFunctionsWorkerMiddleware
{
    private readonly IIdempotentFunctionCatalog _catalog;

    /// <summary>
    /// Creates the middleware over the idempotent-function catalog.
    /// </summary>
    public IdempotencyWorkerMiddleware(IIdempotentFunctionCatalog catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    /// <inheritdoc />
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        IdempotentAttribute? options = _catalog.Find(context);
        HttpRequestData? request = await context.GetHttpRequestDataAsync();
        if (options is null || request is null)
        {
            await next(context);
            return;
        }

        var executor = context.InstanceServices.GetRequiredService<IIdempotencyExecutor>();
        var exchange = new HttpRequestDataIdempotencyExchange(context, request);
        await executor.ExecuteAsync(exchange, options, () => next(context), context.CancellationToken);
    }
}
