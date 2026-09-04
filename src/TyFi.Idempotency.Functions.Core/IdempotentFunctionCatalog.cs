using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Azure.Functions.Worker;

namespace TyFi.Idempotency.Functions;

/// <summary>
/// Resolves and caches, per function, whether it carries an <see cref="IdempotentAttribute"/>.
/// The target method is found from <see cref="FunctionDefinition.EntryPoint"/> via reflection
/// across the loaded application assemblies, so functions may live in any host assembly.
/// </summary>
public sealed class IdempotentFunctionCatalog : IIdempotentFunctionCatalog
{
    private readonly ConcurrentDictionary<string, IdempotentAttribute?> _byEntryPoint = new();

    /// <inheritdoc />
    public IdempotentAttribute? Find(FunctionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return _byEntryPoint.GetOrAdd(
            context.FunctionDefinition.EntryPoint,
            static entryPoint =>
            {
                int lastDot = entryPoint.LastIndexOf('.');
                if (lastDot <= 0)
                {
                    return null;
                }

                string typeName = entryPoint[..lastDot];
                string methodName = entryPoint[(lastDot + 1)..];

                Type? type = ResolveType(typeName);
                MethodInfo? method = type?.GetMethod(methodName);
                return method?.GetCustomAttribute<IdempotentAttribute>();
            });
    }

    private static Type? ResolveType(string typeName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? type = assembly.GetType(typeName, throwOnError: false);
            if (type is not null)
            {
                return type;
            }
        }

        return Type.GetType(typeName, throwOnError: false);
    }
}
