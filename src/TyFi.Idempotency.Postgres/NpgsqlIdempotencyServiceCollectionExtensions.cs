using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace TyFi.Idempotency.Postgres;

/// <summary>
/// Registers the transactional PostgreSQL idempotency store.
/// </summary>
public static class NpgsqlIdempotencyServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="NpgsqlIdempotencyStore"/> as the <see cref="IIdempotencyStore"/>.
    /// The host must separately register an <see cref="IIdempotencyDbSession"/> (scoped) and
    /// wire it as the <see cref="IIdempotencyUnitOfWork"/> so the ledger shares the command's
    /// transaction.
    /// </summary>
    public static IServiceCollection AddNpgsqlIdempotencyStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IIdempotencyStore, NpgsqlIdempotencyStore>();
        return services;
    }
}
