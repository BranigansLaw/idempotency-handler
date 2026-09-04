using System.Security.Cryptography;
using System.Text;

namespace TyFi.Idempotency;

/// <summary>
/// Computes the request hash that binds an idempotency key to a specific request, so the
/// same key reused with a different payload can be rejected.
/// </summary>
public interface IIdempotencyRequestHasher
{
    /// <summary>
    /// Hashes a canonical form of the request.
    /// </summary>
    byte[] Hash(string scope, string method, string pathAndQuery, byte[] body);
}

/// <summary>
/// Default hasher: SHA-256 over a canonical form of <c>(scope, method, path + query, body)</c>.
/// </summary>
public sealed class Sha256IdempotencyRequestHasher : IIdempotencyRequestHasher
{
    /// <inheritdoc />
    public byte[] Hash(string scope, string method, string pathAndQuery, byte[] body)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(pathAndQuery);
        ArgumentNullException.ThrowIfNull(body);

        byte[] prefix = Encoding.UTF8.GetBytes(
            string.Join('\n', scope, method, pathAndQuery, string.Empty));

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData(prefix);
        sha.AppendData(body);
        return sha.GetHashAndReset();
    }
}
