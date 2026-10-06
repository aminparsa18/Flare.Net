using System.Globalization;
using System.Security.Cryptography;
using Flare.Api.Model;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Flare.Api.Alerting;

/// <summary>What a valid signed ack link says: which rule it acknowledges, and when it was issued.</summary>
public readonly record struct AlertAckLinkClaims(Guid RuleId, DateTimeOffset IssuedAt);

/// <summary>
/// Issues and checks the signed link a firing notification carries as <c>{{ack_url}}</c>
/// (ADR-0127). The link is the credential, so it is authenticated and encrypted, expires, and is
/// bound to the rule and the moment it was sent.
/// </summary>
public interface IAlertAckLinkSigner
{
    /// <summary>The dashboard link for <paramref name="ruleId"/>, or null when <see cref="AlertLinkOptions.PublicUrl"/> is blank (there is no usable base).</summary>
    string? CreateUrl(Guid ruleId, DateTimeOffset issuedAt);

    /// <summary>The claims behind <paramref name="token"/>; null when it is malformed, tampered with or expired.</summary>
    AlertAckLinkClaims? Validate(string? token);
}

/// <summary>
/// <see cref="IAlertAckLinkSigner"/> over ASP.NET Core Data Protection. Flare.Api and
/// Flare.AlertWorker persist the same key ring to Redis, so the worker that sends the
/// notification and the API that redeems the link share keys with no new setting to manage.
/// </summary>
public sealed class AlertAckLinkSigner(IDataProtectionProvider provider, IOptions<AlertLinkOptions> options) : IAlertAckLinkSigner
{
    // Bump the suffix to invalidate every outstanding link.
    private const string Purpose = "Flare.AlertAckLink.v1";

    private readonly ITimeLimitedDataProtector protector = provider.CreateProtector(Purpose).ToTimeLimitedDataProtector();

    public string? CreateUrl(Guid ruleId, DateTimeOffset issuedAt)
    {
        var baseUrl = options.Value.PublicUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return null;
        }

        var payload = $"{ruleId:N}|{issuedAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)}";
        var lifetime = TimeSpan.FromHours(Math.Clamp(options.Value.AckLinkLifetimeHours, 1, 24 * 14));
        var token = protector.Protect(payload, lifetime);
        return $"{baseUrl.TrimEnd('/')}/ack?token={Uri.EscapeDataString(token)}";
    }

    public AlertAckLinkClaims? Validate(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            var parts = protector.Unprotect(token).Split('|');
            return parts.Length == 2
                && Guid.TryParseExact(parts[0], "N", out var ruleId)
                && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var issuedMs)
                    ? new AlertAckLinkClaims(ruleId, DateTimeOffset.FromUnixTimeMilliseconds(issuedMs))
                    : null;
        }
        catch (CryptographicException)
        {
            // Covers a bad payload, a signature from another key ring, and an expired link alike.
            return null;
        }
    }
}
