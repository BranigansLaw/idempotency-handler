using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.DependencyInjection;

namespace TyFi.Idempotency.Functions.AspNetCore;

/// <summary>
/// Isolated-worker middleware that applies the idempotency protocol to ASP.NET Core
/// integration functions marked with <see cref="IdempotentAttribute"/>. It is a thin
/// adapter: it wraps the <see cref="HttpContext"/> in an exchange and delegates the
/// protocol to the shared <see cref="IIdempotencyExecutor"/>.
/// </summary>
public sealed class IdempotencyMiddleware : IFunctionsWorkerMiddleware
{
    private readonly IIdempotentFunctionCatalog _catalog;

    /// <summary>
    /// Creates the middleware over the idempotent-function catalog.
    /// </summary>
    public IdempotencyMiddleware(IIdempotentFunctionCatalog catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    /// <inheritdoc />
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        IdempotentAttribute? options = _catalog.Find(context);
        HttpContext? http = context.GetHttpContext();
        if (options is null || http is null)
        {
            await next(context);
            return;
        }

        var executor = context.InstanceServices.GetRequiredService<IIdempotencyExecutor>();
        var exchange = new HttpContextIdempotencyExchange(http);
        await executor.ExecuteAsync(exchange, options, () => next(context), context.CancellationToken);
    }
}
