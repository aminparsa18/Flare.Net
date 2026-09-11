using System.Security.Cryptography;
using System.Text;

namespace Flare.Identity.PersonalAccessTokens;

/// <summary>
/// Hashing for personal access tokens - deliberately the same SHA-256-of-a-256-bit-CSPRNG
/// scheme as <see cref="IngestKeys.IngestApiKeyHasher"/> and for the same reason: this is a
/// per-request bearer-token check (<see cref="Auth.SessionAuthenticationHandler"/>), not a
/// login, and a 256-bit random token has no brute-force surface worth slowing down for
/// with PBKDF2. Kept as its own type rather than reusing <c>IngestApiKeyHasher</c> so the
/// two token families (machine-scoped ingest keys vs. user-scoped access tokens) stay
/// independently evolvable - e.g. <see cref="Prefix"/> exists here only because a raw
/// token is something a person copy-pastes into a script/CI secret and benefits from
/// being recognizable at a glance (same idea as GitHub's <c>ghp_</c>/<c>github_pat_</c>
/// prefixes); ingest keys have no equivalent need.
/// </summary>
public static class PersonalAccessTokenHasher
{
    /// <summary>Prepended to every raw token so it's identifiable in logs, secret
    /// scanners, and by a person glancing at a config file - never itself secret.</summary>
    public const string Prefix = "flr_pat_";

    public static string Hash(string rawToken) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    /// <summary>Generates a new <see cref="Prefix"/>-prefixed token wrapping 256 bits of
    /// CSPRNG output, base64url-encoded (safe as both an <c>Authorization: Bearer</c>
    /// header value and something an operator can copy/paste).</summary>
    public static string GenerateRawToken() =>
        Prefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
