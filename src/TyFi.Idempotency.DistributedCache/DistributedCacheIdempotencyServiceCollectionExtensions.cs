using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace TyFi.Idempotency.DistributedCache;

/// <summary>
/// Registers the best-effort distributed-cache idempotency store.
/// </summary>
public static class DistributedCacheIdempotencyServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="DistributedCacheIdempotencyStore"/> as the
    /// <see cref="IIdempotencyStore"/> and a <see cref="NoOpIdempotencyUnitOfWork"/> (this
    /// tier provides no transactional coupling). An <see cref="IDistributedCache"/> must be
    /// registered separately.
    /// </summary>
    public static IServiceCollection AddDistributedCacheIdempotencyStore(
        this IServiceCollection services,
        Action<DistributedCacheIdempotencyOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions<DistributedCacheIdempotencyOptions>();
        }

        services.TryAddSingleton<IIdempotencyUnitOfWork, NoOpIdempotencyUnitOfWork>();
        services.TryAddScoped<IIdempotencyStore, DistributedCacheIdempotencyStore>();
        return services;
    }
}
