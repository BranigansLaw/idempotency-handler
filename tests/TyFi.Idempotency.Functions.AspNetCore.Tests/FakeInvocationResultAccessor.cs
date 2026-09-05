namespace TyFi.Idempotency.Functions.AspNetCore.Tests;

/// <summary>
/// A simple <see cref="IInvocationResultAccessor"/> whose value the test's <c>next</c>
/// delegate sets (mirroring how the function stores its <c>IActionResult</c>) and that the
/// exchange reads and replaces.
/// </summary>
internal sealed class FakeInvocationResultAccessor : IInvocationResultAccessor
{
    public object? Value { get; set; }
}
