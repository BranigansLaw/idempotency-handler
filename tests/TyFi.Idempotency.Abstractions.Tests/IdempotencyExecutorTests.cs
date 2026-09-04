using System.Text;
using Xunit;

namespace TyFi.Idempotency.Tests;

public sealed class IdempotencyExecutorTests
{
    private static readonly IdempotentAttribute Options = new();

    [Fact]
    public async Task Claimed_success_runs_the_function_stores_and_commits()
    {
        var exchange = new FakeExchange("Bearer x", "key-1") { NextStatusCode = 201, NextBody = Bytes("created") };
        var uow = new FakeUnitOfWork();
        var store = new FakeIdempotencyStore(IdempotencyClaimResult.Claimed());

        await BuildExecutor(uow, store).ExecuteAsync(exchange, Options, NoOp, CancellationToken.None);

        Assert.True(exchange.Invoked);
        Assert.True(store.Completed);
        Assert.Equal(201, store.CompletedStatus);
        Assert.Equal(1, uow.Commits);
        Assert.Equal(0, uow.Rollbacks);
    }

    [Fact]
    public async Task Claimed_failure_rolls_back_and_does_not_store()
    {
        var exchange = new FakeExchange("Bearer x", "key-1") { NextStatusCode = 400, NextBody = Bytes("bad") };
        var uow = new FakeUnitOfWork();
        var store = new FakeIdempotencyStore(IdempotencyClaimResult.Claimed());

        await BuildExecutor(uow, store).ExecuteAsync(exchange, Options, NoOp, CancellationToken.None);

        Assert.True(exchange.Invoked);
        Assert.False(store.Completed);
        Assert.Equal(0, uow.Commits);
        Assert.Equal(1, uow.Rollbacks);
    }

    [Fact]
    public async Task Completed_duplicate_replays_the_stored_response_without_running_the_function()
    {
        var exchange = new FakeExchange("Bearer x", "key-1");
        var uow = new FakeUnitOfWork();
        var store = new FakeIdempotencyStore(IdempotencyClaimResult.Completed(201, Bytes("replayed")));
        var functionRan = false;

        await BuildExecutor(uow, store).ExecuteAsync(
            exchange, Options, () => { functionRan = true; return Task.CompletedTask; }, CancellationToken.None);

        Assert.False(functionRan);
        Assert.Equal(0, uow.Commits);
        Assert.Equal(201, exchange.ShortCircuitStatus);
        Assert.Equal("replayed", exchange.ShortCircuitBody);
    }

    [Fact]
    public async Task Request_mismatch_returns_conflict_without_running_the_function()
    {
        var exchange = new FakeExchange("Bearer x", "key-1");
        var store = new FakeIdempotencyStore(IdempotencyClaimResult.RequestMismatch());

        await BuildExecutor(new FakeUnitOfWork(), store).ExecuteAsync(
            exchange, Options, ShouldNotRun, CancellationToken.None);

        Assert.Equal(409, exchange.ShortCircuitStatus);
        Assert.Null(exchange.ShortCircuitRetryAfter);
    }

    [Fact]
    public async Task In_progress_returns_conflict_with_retry_after()
    {
        var exchange = new FakeExchange("Bearer x", "key-1");
        var store = new FakeIdempotencyStore(IdempotencyClaimResult.InProgress());

        await BuildExecutor(new FakeUnitOfWork(), store).ExecuteAsync(
            exchange, Options, ShouldNotRun, CancellationToken.None);

        Assert.Equal(409, exchange.ShortCircuitStatus);
        Assert.NotNull(exchange.ShortCircuitRetryAfter);
    }

    [Fact]
    public async Task Missing_key_runs_the_function_without_a_transaction()
    {
        var exchange = new FakeExchange("Bearer x", idempotencyKey: null);
        var uow = new FakeUnitOfWork();
        var functionRan = false;

        await BuildExecutor(uow, new FakeIdempotencyStore(IdempotencyClaimResult.Claimed()))
            .ExecuteAsync(exchange, Options, () => { functionRan = true; return Task.CompletedTask; }, CancellationToken.None);

        Assert.True(functionRan);
        Assert.Equal(0, uow.Begins);
        Assert.Null(exchange.ShortCircuitStatus);
    }

    [Fact]
    public async Task Missing_key_when_required_returns_bad_request()
    {
        var exchange = new FakeExchange("Bearer x", idempotencyKey: null);
        var uow = new FakeUnitOfWork();
        var options = new IdempotentAttribute { KeyRequired = true };

        await BuildExecutor(uow, new FakeIdempotencyStore(IdempotencyClaimResult.Claimed()))
            .ExecuteAsync(exchange, options, ShouldNotRun, CancellationToken.None);

        Assert.Equal(400, exchange.ShortCircuitStatus);
        Assert.Equal(0, uow.Begins);
    }

    [Fact]
    public async Task Unscoped_request_runs_the_function_without_a_transaction()
    {
        var exchange = new FakeExchange(authorization: null, "key-1");
        var uow = new FakeUnitOfWork();
        var functionRan = false;

        await BuildExecutor(uow, new FakeIdempotencyStore(IdempotencyClaimResult.Claimed()))
            .ExecuteAsync(exchange, Options, () => { functionRan = true; return Task.CompletedTask; }, CancellationToken.None);

        Assert.True(functionRan);
        Assert.Equal(0, uow.Begins);
    }

    [Fact]
    public async Task A_thrown_function_rolls_back_and_rethrows()
    {
        var exchange = new FakeExchange("Bearer x", "key-1");
        var uow = new FakeUnitOfWork();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BuildExecutor(uow, new FakeIdempotencyStore(IdempotencyClaimResult.Claimed()))
                .ExecuteAsync(exchange, Options, () => throw new InvalidOperationException(), CancellationToken.None));

        Assert.Equal(0, uow.Commits);
        Assert.Equal(1, uow.Rollbacks);
    }

    private static IdempotencyExecutor BuildExecutor(IIdempotencyUnitOfWork uow, IIdempotencyStore store) =>
        new(
            uow,
            store,
            new FakeScopeProvider(),
            new Sha256IdempotencyRequestHasher(),
            new SuccessStatusIdempotencyResponsePolicy());

    private static Task NoOp() => Task.CompletedTask;

    private static Task ShouldNotRun() => throw new Xunit.Sdk.XunitException("The function should not have run.");

    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);
}
