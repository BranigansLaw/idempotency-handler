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

    /// <summary>
    /// Registers a custom <see cref="IIdempotencyStore"/> as the scoped store. Use this when
    /// you back idempotency with your own storage (Cosmos DB, DynamoDB, SQL Server, …).
    /// </summary>
    /// <remarks>
    /// A best-effort <see cref="NoOpIdempotencyUnitOfWork"/> is registered by default so the
    /// store works without a transaction. If your store enlists in a business transaction,
    /// register your own <see cref="IIdempotencyUnitOfWork"/> before calling this and it will
    /// be kept (registration uses "try add").
    /// </remarks>
    /// <typeparam name="TStore">The custom store implementation.</typeparam>
    public static IServiceCollection AddIdempotencyStore<TStore>(this IServiceCollection services)
        where TStore : class, IIdempotencyStore
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IIdempotencyStore, TStore>();
        services.TryAddSingleton<IIdempotencyUnitOfWork, NoOpIdempotencyUnitOfWork>();

        return services;
    }
}
