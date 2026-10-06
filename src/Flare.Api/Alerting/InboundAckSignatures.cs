using System.Security.Cryptography;
using System.Text;

namespace Flare.Api.Alerting;

/// <summary>
/// Request-signature checks for the inbound ack integrations (ADR-0138). Pure, so they are
/// unit-tested; the endpoints refuse anything these reject before reading a single field.
/// </summary>
public static class InboundAckSignatures
{
    /// <summary>How far a Slack request timestamp may be from now (Slack's own guidance), which bounds replays.</summary>
    public static readonly TimeSpan SlackTolerance = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Slack's <c>X-Slack-Signature</c>: <c>v0=</c> plus hex HMAC-SHA256 of <c>v0:{timestamp}:{body}</c>
    /// under the app's signing secret, with a request timestamp within <see cref="SlackTolerance"/>.
    /// </summary>
    public static bool IsValidSlack(string secret, string? timestamp, string? signature, string body, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(signature) || !long.TryParse(timestamp, out var seconds))
        {
            return false;
        }

        var sent = DateTimeOffset.FromUnixTimeSeconds(seconds);
        if ((now - sent).Duration() > SlackTolerance)
        {
            return false;
        }

        return Matches($"v0={Hex(Hmac(secret, Encoding.UTF8.GetBytes($"v0:{timestamp}:{body}")))}", signature);
    }

    /// <summary>
    /// PagerDuty's <c>X-PagerDuty-Signature</c>: one or more comma-separated <c>v1=</c> hex HMAC-SHA256
    /// values of the raw body (several while the subscription secret is being rotated); any match passes.
    /// </summary>
    public static bool IsValidPagerDuty(string secret, string? header, ReadOnlySpan<byte> body)
    {
        if (string.IsNullOrEmpty(secret) || string.IsNullOrWhiteSpace(header))
        {
            return false;
        }

        var expected = $"v1={Hex(Hmac(secret, body))}";
        foreach (var candidate in header.Split(','))
        {
            if (Matches(expected, candidate.Trim()))
            {
                return true;
            }
        }

        return false;
    }

    private static byte[] Hmac(string secret, ReadOnlySpan<byte> data) => HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), data);

    private static string Hex(byte[] bytes) => Convert.ToHexStringLower(bytes);

    private static bool Matches(string expected, string actual) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));
}
