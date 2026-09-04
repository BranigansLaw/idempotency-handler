using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace TyFi.Idempotency.Functions.AspNetCore;

/// <summary>
/// Wires idempotency into an ASP.NET Core integration Functions host.
/// </summary>
public static class IdempotencyBuilderExtensions
{
    /// <summary>
    /// Registers the idempotency core and the attribute catalog. A host must also register
    /// an <see cref="IIdempotencyStore"/>, an <see cref="IIdempotencyUnitOfWork"/>, and an
    /// <see cref="IIdempotencyScopeProvider"/> (see the store packages).
    /// </summary>
    public static IServiceCollection AddIdempotency(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddIdempotencyCore();
        services.TryAddSingleton<IIdempotentFunctionCatalog, IdempotentFunctionCatalog>();
        return services;
    }

    /// <summary>
    /// Registers idempotency services and scopes the middleware to functions carrying the
    /// <see cref="IdempotentAttribute"/> via cached reflection (not a function-name list).
    /// </summary>
    public static IFunctionsWorkerApplicationBuilder UseIdempotency(
        this IFunctionsWorkerApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddIdempotency();
        builder.UseWhen<IdempotencyMiddleware>(static context =>
            context.InstanceServices.GetRequiredService<IIdempotentFunctionCatalog>().Find(context) is not null);
        return builder;
    }
}
