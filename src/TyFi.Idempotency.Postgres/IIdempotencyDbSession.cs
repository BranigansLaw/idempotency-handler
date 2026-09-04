using Npgsql;

namespace TyFi.Idempotency.Postgres;

/// <summary>
/// A scoped database session the host provides: the ambient <see cref="IIdempotencyUnitOfWork"/>
/// plus the Npgsql connection and transaction that the idempotency store enlists in, so the
/// ledger row and the command's side effect commit or roll back together.
/// </summary>
public interface IIdempotencyDbSession : IIdempotencyUnitOfWork
{
    /// <summary>
    /// The ambient connection; throws when no transaction is open.
    /// </summary>
    NpgsqlConnection Connection { get; }

    /// <summary>
    /// The ambient transaction; throws when no transaction is open.
    /// </summary>
    NpgsqlTransaction Transaction { get; }
}
