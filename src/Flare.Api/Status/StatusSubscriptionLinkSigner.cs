using System.Security.Cryptography;
using Flare.Api.Alerting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Flare.Api.Status;

/// <summary>Which page and address a valid subscription link is for.</summary>
public readonly record struct StatusSubscriptionClaims(Guid PageId, string Email);

/// <summary>
/// Issues and checks the signed links in subscription emails (ADR-0162): one to confirm an address, one to
/// unsubscribe it. The link is the credential, so it is authenticated and encrypted and bound to a page and an
/// address; the two kinds use separate keys, so a confirm link cannot unsubscribe and the reverse.
/// </summary>
public interface IStatusSubscriptionLinkSigner
{
    /// <summary>The dashboard link that confirms <paramref name="email"/>, or null when <see cref="AlertLinkOptions.PublicUrl"/> is blank.</summary>
    string? ConfirmUrl(Guid pageId, string email);

    /// <summary>The dashboard link that unsubscribes <paramref name="email"/>, or null when <see cref="AlertLinkOptions.PublicUrl"/> is blank.</summary>
    string? UnsubscribeUrl(Guid pageId, string email);

    /// <summary>The claims behind a confirm <paramref name="token"/>; null when malformed, tampered with or expired.</summary>
    StatusSubscriptionClaims? ValidateConfirm(string? token);

    /// <summary>The claims behind an unsubscribe <paramref name="token"/>; null when malformed or tampered with.</summary>
    StatusSubscriptionClaims? ValidateUnsubscribe(string? token);
}

public sealed class StatusSubscriptionLinkSigner(IDataProtectionProvider provider, IOptions<AlertLinkOptions> options) : IStatusSubscriptionLinkSigner
{
    // Bump a suffix to invalidate every outstanding link of that kind.
    private const string ConfirmPurpose = "Flare.StatusSubscription.Confirm.v1";
    private const string UnsubscribePurpose = "Flare.StatusSubscription.Unsubscribe.v1";

    private static readonly TimeSpan ConfirmLifetime = TimeSpan.FromDays(3);

    // An unsubscribe link sits in every email, so it must keep working for as long as someone might still hold one.
    private static readonly TimeSpan UnsubscribeLifetime = TimeSpan.FromDays(3650);

    private readonly ITimeLimitedDataProtector confirm = provider.CreateProtector(ConfirmPurpose).ToTimeLimitedDataProtector();
    private readonly ITimeLimitedDataProtector unsubscribe = provider.CreateProtector(UnsubscribePurpose).ToTimeLimitedDataProtector();

    public string? ConfirmUrl(Guid pageId, string email) => Url("confirm", confirm, ConfirmLifetime, pageId, email);

    public string? UnsubscribeUrl(Guid pageId, string email) => Url("unsubscribe", unsubscribe, UnsubscribeLifetime, pageId, email);

    public StatusSubscriptionClaims? ValidateConfirm(string? token) => Validate(confirm, token);

    public StatusSubscriptionClaims? ValidateUnsubscribe(string? token) => Validate(unsubscribe, token);

    private string? Url(string route, ITimeLimitedDataProtector protector, TimeSpan lifetime, Guid pageId, string email)
    {
        var baseUrl = options.Value.PublicUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return null;
        }

        var token = protector.Protect($"{pageId:N}|{email}", lifetime);
        return $"{baseUrl.TrimEnd('/')}/subscribe/{route}?token={Uri.EscapeDataString(token)}";
    }

    private static StatusSubscriptionClaims? Validate(ITimeLimitedDataProtector protector, string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            var payload = protector.Unprotect(token);
            var at = payload.IndexOf('|', StringComparison.Ordinal);
            return at > 0 && Guid.TryParseExact(payload[..at], "N", out var pageId)
                ? new StatusSubscriptionClaims(pageId, payload[(at + 1)..])
                : null;
        }
        catch (CryptographicException)
        {
            // Covers a bad payload, a signature from another key ring or purpose, and an expired link alike.
            return null;
        }
    }
}
