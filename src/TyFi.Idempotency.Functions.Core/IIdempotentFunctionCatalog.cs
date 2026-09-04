using Microsoft.Azure.Functions.Worker;

namespace TyFi.Idempotency.Functions;

/// <summary>
/// Resolves the <see cref="IdempotentAttribute"/> for an executing function, if any.
/// </summary>
public interface IIdempotentFunctionCatalog
{
    /// <summary>
    /// Returns the function's idempotency options, or <see langword="null"/> when it is
    /// not marked idempotent.
    /// </summary>
    IdempotentAttribute? Find(FunctionContext context);
}
