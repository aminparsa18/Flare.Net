using System.Security.Cryptography;
using System.Text;

namespace Flare.Identity.IngestKeys;

/// <summary>
/// Shared SHA-256 hashing for ingest API keys - used both by <c>Flare.Api</c> (hashing a
/// newly-generated raw key before storing it, in <see cref="SqliteIngestApiKeyStore.CreateAsync"/>)
/// and by <c>Flare.Ingest</c> (hashing a presented <c>Authorization: Bearer</c> key to
/// check against the cached active-hash set). SHA-256, not PBKDF2: this is a high-QPS
/// per-request check on the ingest hot path, not a login, and a 256-bit random key has no
/// brute-force surface worth slowing down for.
/// </summary>
public static class IngestApiKeyHasher
{
    public static string Hash(string rawKey) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey)));

    /// <summary>Generates a new 256-bit random raw key, base64url-encoded (safe as both
    /// an HTTP header value and something an operator can copy/paste).</summary>
    public static string GenerateRawKey() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
