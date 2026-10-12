using System.Security.Cryptography;
using System.Text;

namespace Flare.Identity.Audit;

/// <summary>
/// The hash-chain function (ADR-0202). Each field is length-prefixed so no two different
/// rows can serialise to the same bytes; the inputs are the stored text of each column
/// (UTC round-trip timestamp, lower-case guid), so a verifier can recompute from an export.
/// </summary>
public static class AuditHash
{
    /// <summary>Round-trip ("O") UTC text: sorts in time order and is what the chain hashes.</summary>
    public static string TimestampText(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    public static string Compute(
        string prevHash,
        string timestamp,
        string? actorId,
        string actorName,
        string actorKind,
        string action,
        string resourceType,
        string? resourceId,
        string route,
        long statusCode,
        string? sourceIp,
        string? changes)
    {
        var builder = new StringBuilder();
        foreach (var field in new[]
        {
            prevHash, timestamp, actorId, actorName, actorKind, action, resourceType,
            resourceId, route, statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture), sourceIp, changes,
        })
        {
            // A null field is distinct from an empty one: "-" versus a length.
            if (field is null)
            {
                builder.Append('-');
            }
            else
            {
                builder.Append(field.Length).Append(':').Append(field);
            }

            builder.Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }
}
