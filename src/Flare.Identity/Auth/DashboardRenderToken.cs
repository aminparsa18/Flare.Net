using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Flare.Identity.Auth;

/// <summary>What a valid render token says: whose access it carries and which dashboard it was minted for.</summary>
public readonly record struct DashboardRenderTokenClaims(Guid UserId, Guid DashboardId);

/// <summary>
/// Issues and checks the short-lived credential <c>Flare.AlertWorker</c> hands its headless browser to render
/// a scheduled dashboard report (ADR-0142). It travels in the session cookie, prefixed
/// <see cref="Prefix"/>, and <see cref="SessionAuthenticationHandler"/> turns it into a read-only (Viewer)
/// principal for the named user.
/// </summary>
public interface IDashboardRenderTokenSigner
{
    string Create(Guid userId, Guid dashboardId, TimeSpan lifetime);

    /// <summary>The claims behind <paramref name="token"/>; null when it is malformed, tampered with or expired.</summary>
    DashboardRenderTokenClaims? Validate(string? token);
}

/// <summary>
/// <see cref="IDashboardRenderTokenSigner"/> over ASP.NET Core Data Protection. Flare.Api and Flare.AlertWorker
/// persist the same key ring to Redis, so the worker that mints the token and the API that accepts it share
/// keys with no new setting to manage - the same arrangement as the acknowledge link (ADR-0127).
/// </summary>
public sealed class DashboardRenderTokenSigner(IDataProtectionProvider provider) : IDashboardRenderTokenSigner
{
    /// <summary>Marks a cookie value as a render token rather than a session id.</summary>
    public const string Prefix = "flr_render_";

    // Bump the suffix to invalidate every outstanding token.
    private const string Purpose = "Flare.DashboardRender.v1";

    private readonly ITimeLimitedDataProtector protector = provider.CreateProtector(Purpose).ToTimeLimitedDataProtector();

    public string Create(Guid userId, Guid dashboardId, TimeSpan lifetime) =>
        Prefix + protector.Protect($"{userId:N}|{dashboardId:N}", lifetime);

    public DashboardRenderTokenClaims? Validate(string? token)
    {
        if (string.IsNullOrEmpty(token) || !token.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            var parts = protector.Unprotect(token[Prefix.Length..]).Split('|');
            return parts.Length == 2
                && Guid.TryParseExact(parts[0], "N", out var userId)
                && Guid.TryParseExact(parts[1], "N", out var dashboardId)
                    ? new DashboardRenderTokenClaims(userId, dashboardId)
                    : null;
        }
        catch (CryptographicException)
        {
            // Covers a bad payload, a signature from another key ring, and an expired token alike.
            return null;
        }
    }
}
