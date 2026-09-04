namespace TyFi.Idempotency;

/// <summary>
/// An ambient unit of work whose lifetime spans a single request, letting a command's
/// side effect and its idempotency ledger commit or roll back together.
/// </summary>
/// <remarks>
/// The transactional store tier relies on this to couple the ledger write to the
/// command. The best-effort (cache) tier uses <see cref="NoOpIdempotencyUnitOfWork"/>,
/// which provides no atomicity.
/// </remarks>
public interface IIdempotencyUnitOfWork
{
    /// <summary>
    /// Whether a transaction is currently open on this unit of work.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Opens a connection and begins a transaction. Throws if one is already open.
    /// </summary>
    Task BeginAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Commits the open transaction.
    /// </summary>
    Task CommitAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Rolls back the open transaction. Safe to call after a commit (no-op).
    /// </summary>
    Task RollbackAsync(CancellationToken cancellationToken);
}

/// <summary>
/// A no-op unit of work for the best-effort store tier, where there is no transaction to
/// couple the ledger write to. Provides none of the strong-tier atomicity guarantees.
/// </summary>
public sealed class NoOpIdempotencyUnitOfWork : IIdempotencyUnitOfWork
{
    /// <inheritdoc />
    public bool IsActive => false;

    /// <inheritdoc />
    public Task BeginAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task RollbackAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
