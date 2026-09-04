using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace TyFi.Idempotency;

/// <summary>
/// Registers the HTTP-model-agnostic idempotency core: the executor, the SHA-256 request
/// hasher, and the default (<c>2xx</c>) response policy. A host must additionally register
/// an <see cref="IIdempotencyStore"/>, an <see cref="IIdempotencyUnitOfWork"/>, and an
/// <see cref="IIdempotencyScopeProvider"/> (the store and adapter packages provide these).
/// </summary>
public static class IdempotencyCoreServiceCollectionExtensions
{
    /// <summary>
    /// Adds the executor, request hasher, and response policy if not already registered.
    /// </summary>
    public static IServiceCollection AddIdempotencyCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IIdempotencyRequestHasher, Sha256IdempotencyRequestHasher>();
        services.TryAddSingleton<IIdempotencyResponsePolicy, SuccessStatusIdempotencyResponsePolicy>();
        services.TryAddScoped<IIdempotencyExecutor, IdempotencyExecutor>();

        return services;
    }
}
