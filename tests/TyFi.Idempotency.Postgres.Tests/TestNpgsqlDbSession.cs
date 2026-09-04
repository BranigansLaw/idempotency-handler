using Npgsql;
using TyFi.Idempotency.Postgres;

namespace TyFi.Idempotency.Postgres.Tests;

/// <summary>
/// A minimal <see cref="IIdempotencyDbSession"/> for the integration tests: it opens a
/// connection and a transaction, and exposes them for the store to enlist in. Real hosts
/// wire their own scoped session (see the therapy repo's NpgsqlDbSession).
/// </summary>
internal sealed class TestNpgsqlDbSession : IIdempotencyDbSession, IAsyncDisposable
{
    private readonly string _connectionString;
    private NpgsqlConnection? _connection;
    private NpgsqlTransaction? _transaction;
    private bool _finalized;

    public TestNpgsqlDbSession(string connectionString) => _connectionString = connectionString;

    public bool IsActive => _connection is not null;

    public NpgsqlConnection Connection =>
        _connection ?? throw new InvalidOperationException("No database session is active.");

    public NpgsqlTransaction Transaction =>
        _transaction ?? throw new InvalidOperationException("No database session is active.");

    public async Task BeginAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        _connection = connection;
        _transaction = await connection.BeginTransactionAsync(cancellationToken);
        _finalized = false;
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        await Transaction.CommitAsync(cancellationToken);
        _finalized = true;
    }

    public async Task RollbackAsync(CancellationToken cancellationToken)
    {
        if (_transaction is null || _finalized)
        {
            return;
        }

        await _transaction.RollbackAsync(cancellationToken);
        _finalized = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
        {
            if (!_finalized)
            {
                await _transaction.RollbackAsync();
            }

            await _transaction.DisposeAsync();
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }
}
