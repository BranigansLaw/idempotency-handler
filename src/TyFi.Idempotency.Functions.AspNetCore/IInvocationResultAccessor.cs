using Microsoft.Azure.Functions.Worker;

namespace TyFi.Idempotency.Functions.AspNetCore;

/// <summary>
/// Reads and replaces the function's invocation result. Under the ASP.NET Core integration
/// the result is an <c>IActionResult</c> the framework executes after the middleware, so
/// this is the seam the exchange uses instead of writing the response body directly.
/// </summary>
internal interface IInvocationResultAccessor
{
    /// <summary>
    /// The current invocation result value (an <c>IActionResult</c> under this integration).
    /// </summary>
    object? Value { get; set; }
}

/// <summary>
/// Default <see cref="IInvocationResultAccessor"/> backed by
/// <c>FunctionContext.GetInvocationResult()</c>.
/// </summary>
internal sealed class FunctionContextInvocationResultAccessor : IInvocationResultAccessor
{
    private readonly FunctionContext _context;

    public FunctionContextInvocationResultAccessor(FunctionContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public object? Value
    {
        get => _context.GetInvocationResult().Value;
        set => _context.GetInvocationResult().Value = value;
    }
}
