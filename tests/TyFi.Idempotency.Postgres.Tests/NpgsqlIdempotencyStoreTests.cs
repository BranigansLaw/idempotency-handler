using System.Text;
using TyFi.Idempotency.Postgres;
using Xunit;

namespace TyFi.Idempotency.Postgres.Tests;

[Collection(PostgresCollection.Name)]
public sealed class NpgsqlIdempotencyStoreTests
{
    private readonly PostgresContainerFixture _postgres;

    public NpgsqlIdempotencyStoreTests(PostgresContainerFixture postgres)
    {
        _postgres = postgres ?? throw new ArgumentNullException(nameof(postgres));
    }

    [Fact]
    public async Task Claim_completes_and_a_duplicate_replays_the_stored_response()
    {
        string scope = NewId();
        const string key = "key-1";
        byte[] hash = Hash("payload-a");

        await using (var session = new TestNpgsqlDbSession(_postgres.ConnectionString))
        {
            await session.BeginAsync(CancellationToken.None);
            var store = new NpgsqlIdempotencyStore(session);
            IdempotencyClaimResult claim = await store.TryClaimAsync(scope, key, hash, CancellationToken.None);
            Assert.Equal(IdempotencyClaimOutcome.Claimed, claim.Outcome);
            await store.CompleteAsync(scope, key, 201, Bytes("created"), CancellationToken.None);
            await session.CommitAsync(CancellationToken.None);
        }

        await using (var session = new TestNpgsqlDbSession(_postgres.ConnectionString))
        {
            await session.BeginAsync(CancellationToken.None);
            var store = new NpgsqlIdempotencyStore(session);
            IdempotencyClaimResult replay = await store.TryClaimAsync(scope, key, hash, CancellationToken.None);
            await session.RollbackAsync(CancellationToken.None);

            Assert.Equal(IdempotencyClaimOutcome.AlreadyCompleted, replay.Outcome);
            Assert.Equal(201, replay.StatusCode);
            Assert.Equal("created", Text(replay.ResponseBody!));
        }
    }

    [Fact]
    public async Task Same_key_with_a_different_payload_is_a_mismatch()
    {
        string scope = NewId();
        const string key = "key-1";

        await using (var session = new TestNpgsqlDbSession(_postgres.ConnectionString))
        {
            await session.BeginAsync(CancellationToken.None);
            var store = new NpgsqlIdempotencyStore(session);
            await store.TryClaimAsync(scope, key, Hash("payload-a"), CancellationToken.None);
            await store.CompleteAsync(scope, key, 201, Bytes("created"), CancellationToken.None);
            await session.CommitAsync(CancellationToken.None);
        }

        await using (var session = new TestNpgsqlDbSession(_postgres.ConnectionString))
        {
            await session.BeginAsync(CancellationToken.None);
            var store = new NpgsqlIdempotencyStore(session);
            IdempotencyClaimResult mismatch =
                await store.TryClaimAsync(scope, key, Hash("payload-b"), CancellationToken.None);
            await session.RollbackAsync(CancellationToken.None);

            Assert.Equal(IdempotencyClaimOutcome.RequestMismatch, mismatch.Outcome);
        }
    }

    [Fact]
    public async Task A_rolled_back_claim_leaves_the_key_reusable()
    {
        string scope = NewId();
        const string key = "key-1";
        byte[] hash = Hash("payload-a");

        await using (var session = new TestNpgsqlDbSession(_postgres.ConnectionString))
        {
            await session.BeginAsync(CancellationToken.None);
            var store = new NpgsqlIdempotencyStore(session);
            IdempotencyClaimResult first = await store.TryClaimAsync(scope, key, hash, CancellationToken.None);
            Assert.Equal(IdempotencyClaimOutcome.Claimed, first.Outcome);
            await session.RollbackAsync(CancellationToken.None);
        }

        await using (var session = new TestNpgsqlDbSession(_postgres.ConnectionString))
        {
            await session.BeginAsync(CancellationToken.None);
            var store = new NpgsqlIdempotencyStore(session);
            IdempotencyClaimResult second = await store.TryClaimAsync(scope, key, hash, CancellationToken.None);
            await session.RollbackAsync(CancellationToken.None);

            Assert.Equal(IdempotencyClaimOutcome.Claimed, second.Outcome);
        }
    }

    [Fact]
    public async Task A_concurrent_in_flight_claim_reports_in_progress()
    {
        string scope = NewId();
        const string key = "key-1";
        byte[] hash = Hash("payload-a");

        await using var holder = new TestNpgsqlDbSession(_postgres.ConnectionString);
        await holder.BeginAsync(CancellationToken.None);
        var holderStore = new NpgsqlIdempotencyStore(holder);
        IdempotencyClaimResult held = await holderStore.TryClaimAsync(scope, key, hash, CancellationToken.None);
        Assert.Equal(IdempotencyClaimOutcome.Claimed, held.Outcome);

        await using (var contender = new TestNpgsqlDbSession(_postgres.ConnectionString))
        {
            await contender.BeginAsync(CancellationToken.None);
            var contenderStore = new NpgsqlIdempotencyStore(contender);
            IdempotencyClaimResult racing = await contenderStore.TryClaimAsync(scope, key, hash, CancellationToken.None);
            await contender.RollbackAsync(CancellationToken.None);

            Assert.Equal(IdempotencyClaimOutcome.InProgress, racing.Outcome);
        }

        await holder.RollbackAsync(CancellationToken.None);
    }

    private static string NewId() => Guid.NewGuid().ToString("N");

    private static byte[] Hash(string value) => Encoding.UTF8.GetBytes("hash:" + value);

    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);

    private static string Text(byte[] value) => Encoding.UTF8.GetString(value);
}
