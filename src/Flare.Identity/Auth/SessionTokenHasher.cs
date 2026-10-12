using System.Security.Cryptography;
using System.Text;

namespace Flare.Identity.Auth;

/// <summary>
/// SHA-256 of a session cookie token, hex. <c>Sessions.Id</c> stores this digest, never the token, so a
/// read of the identity database (backup, replica, SQL injection) can't be replayed as a login - the same
/// scheme as <see cref="PersonalAccessTokens.PersonalAccessTokenHasher"/> and the ingest keys. A plain
/// fast hash is enough: the input is 256 bits of CSPRNG output, so there is nothing to brute-force.
/// </summary>
public static class SessionTokenHasher
{
    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
