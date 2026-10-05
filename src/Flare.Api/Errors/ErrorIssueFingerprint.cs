using System.Security.Cryptography;
using System.Text;

namespace Flare.Api.Errors;

/// <summary>
/// The stable identity of an exception group. Groups are exact <c>(exception.type,
/// exception.message)</c> pairs (see <c>ExceptionGroup</c>), so the fingerprint is a hash of
/// that pair - computed here rather than in SQL so the dashboard never needs to know it: it
/// matches state to groups by (type, message) and the server derives the key on write.
/// </summary>
public static class ErrorIssueFingerprint
{
    /// <summary>Separates type from message in <see cref="Key"/> - the ASCII unit separator, which exception text doesn't contain in practice.</summary>
    public const char KeySeparator = '\u001f';

    /// <summary>The (type, message) pair as one string, the form <c>ErrorIssueEvidenceQueryBuilder</c> matches against in SQL.</summary>
    public static string Key(string exceptionType, string exceptionMessage) => exceptionType + KeySeparator + exceptionMessage;

    /// <summary>32 lowercase hex chars (first 128 bits of SHA-256).</summary>
    public static string Compute(string exceptionType, string exceptionMessage) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Key(exceptionType, exceptionMessage))).AsSpan(0, 16));
}
