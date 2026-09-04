using System.Reflection;

namespace TyFi.Idempotency.Postgres;

/// <summary>
/// The DDL for the idempotency ledger table. Run <see cref="CreateTableSql"/> from your
/// migration tooling (the same script is also packaged under <c>scripts/</c>).
/// </summary>
public static class NpgsqlIdempotencySchema
{
    private const string ResourceName =
        "TyFi.Idempotency.Postgres.Scripts.Script0001_IdempotencyKeys.sql";

    private static readonly Lazy<string> Ddl = new(ReadEmbeddedScript);

    /// <summary>
    /// The <c>CREATE TABLE IF NOT EXISTS idempotency_keys</c> statement.
    /// </summary>
    public static string CreateTableSql => Ddl.Value;

    private static string ReadEmbeddedScript()
    {
        Assembly assembly = typeof(NpgsqlIdempotencySchema).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded script '{ResourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
