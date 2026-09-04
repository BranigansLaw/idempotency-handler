using Npgsql;

namespace TyFi.Idempotency.Postgres;

/// <summary>
/// PostgreSQL-backed idempotency ledger (the strong, transactional tier). Runs on the
/// ambient <see cref="IIdempotencyDbSession"/> transaction so the claim row and the
/// command's side effect are atomic. A concurrent duplicate blocks on the primary key
/// until the original commits (then replays) or rolls back (then proceeds); a bounded lock
/// timeout surfaces a still-running original as <see cref="IdempotencyClaimOutcome.InProgress"/>.
/// </summary>
public sealed class NpgsqlIdempotencyStore : IIdempotencyStore
{
    // Bounds how long a concurrent duplicate blocks before reporting "in progress".
    private const string LockTimeout = "3s";

    private readonly IIdempotencyDbSession _session;

    /// <summary>
    /// Creates the store over the ambient database session.
    /// </summary>
    public NpgsqlIdempotencyStore(IIdempotencyDbSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    /// <inheritdoc />
    public async Task<IdempotencyClaimResult> TryClaimAsync(
        string scope,
        string key,
        byte[] requestHash,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(requestHash);

        await using (var setTimeout = _session.Connection.CreateCommand())
        {
            setTimeout.Transaction = _session.Transaction;
            setTimeout.CommandText = $"SET LOCAL lock_timeout = '{LockTimeout}'";
            await setTimeout.ExecuteNonQueryAsync(cancellationToken);
        }

        try
        {
            await using var claim = _session.Connection.CreateCommand();
            claim.Transaction = _session.Transaction;
            claim.CommandText =
                "INSERT INTO idempotency_keys (scope, idempotency_key, request_hash) " +
                "VALUES (@scope, @key, @hash) " +
                "ON CONFLICT (scope, idempotency_key) DO NOTHING " +
                "RETURNING scope";
            claim.Parameters.AddWithValue("scope", scope);
            claim.Parameters.AddWithValue("key", key);
            claim.Parameters.AddWithValue("hash", requestHash);

            object? won = await claim.ExecuteScalarAsync(cancellationToken);
            if (won is not null)
            {
                return IdempotencyClaimResult.Claimed();
            }
        }
        catch (PostgresException exception)
            when (exception.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            return IdempotencyClaimResult.InProgress();
        }

        await using var existing = _session.Connection.CreateCommand();
        existing.Transaction = _session.Transaction;
        existing.CommandText =
            "SELECT request_hash, response_status_code, response_body " +
            "FROM idempotency_keys WHERE scope = @scope AND idempotency_key = @key";
        existing.Parameters.AddWithValue("scope", scope);
        existing.Parameters.AddWithValue("key", key);

        await using var reader = await existing.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            // The conflicting row disappeared (its owner rolled back after our insert lost
            // the race); treat as still in progress so the caller retries.
            return IdempotencyClaimResult.InProgress();
        }

        var storedHash = (byte[])reader["request_hash"];
        if (!storedHash.AsSpan().SequenceEqual(requestHash))
        {
            return IdempotencyClaimResult.RequestMismatch();
        }

        if (await reader.IsDBNullAsync(1, cancellationToken))
        {
            return IdempotencyClaimResult.InProgress();
        }

        int statusCode = reader.GetInt32(1);
        var body = (byte[])reader["response_body"];
        return IdempotencyClaimResult.Completed(statusCode, body);
    }

    /// <inheritdoc />
    public async Task CompleteAsync(
        string scope,
        string key,
        int statusCode,
        byte[] responseBody,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(responseBody);

        await using var command = _session.Connection.CreateCommand();
        command.Transaction = _session.Transaction;
        command.CommandText =
            "UPDATE idempotency_keys " +
            "SET response_status_code = @status, response_body = @body " +
            "WHERE scope = @scope AND idempotency_key = @key";
        command.Parameters.AddWithValue("status", statusCode);
        command.Parameters.AddWithValue("body", responseBody);
        command.Parameters.AddWithValue("scope", scope);
        command.Parameters.AddWithValue("key", key);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
