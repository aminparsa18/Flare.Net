using Microsoft.AspNetCore.DataProtection;

namespace Flare.Api.Alerting;

/// <summary>
/// Encrypts notification credentials (webhook URLs, bot tokens, routing/API keys) before they
/// are written to ClickHouse and decrypts them as they are read back (ADR-0205).
/// </summary>
public interface INotificationSecretProtector
{
    /// <summary>The value to store: "" stays "", an already-protected value is returned unchanged, anything else is encrypted.</summary>
    string Protect(string value);

    /// <summary>The plaintext for a stored value. A value without the protected prefix is a row written before encryption existed and is returned as is.</summary>
    string Unprotect(string stored);

    /// <summary>True for a non-empty stored value that is still plaintext.</summary>
    bool NeedsProtection(string stored);
}

/// <summary>
/// <see cref="INotificationSecretProtector"/> over ASP.NET Core Data Protection. Flare.Api and
/// Flare.AlertWorker persist one key ring to Redis, so the API that stores a secret and the
/// worker that sends with it share keys. Data Protection rolls its keys over on its own and
/// keeps old ones for decryption, so a value stays readable across rotations and is re-protected
/// under the newest key the next time it is saved.
/// </summary>
public sealed class NotificationSecretProtector(IDataProtectionProvider provider) : INotificationSecretProtector
{
    // Bump the suffix only together with a migration of the stored values.
    private const string Purpose = "Flare.NotificationSecrets.v1";

    /// <summary>Marks a stored value as ciphertext; the version leaves room to change the scheme.</summary>
    internal const string Prefix = "enc:v1:";

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    public string Protect(string value) =>
        string.IsNullOrEmpty(value) || value.StartsWith(Prefix, StringComparison.Ordinal)
            ? value
            : Prefix + _protector.Protect(value);

    public string Unprotect(string stored) =>
        !string.IsNullOrEmpty(stored) && stored.StartsWith(Prefix, StringComparison.Ordinal)
            ? _protector.Unprotect(stored[Prefix.Length..])
            : stored;

    public bool NeedsProtection(string stored) =>
        !string.IsNullOrEmpty(stored) && !stored.StartsWith(Prefix, StringComparison.Ordinal);
}
