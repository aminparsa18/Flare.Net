namespace Flare.Identity.PasswordSetTokens;

public enum PasswordSetPurpose
{
    Invite,
    Reset,
}

/// <summary>A freshly minted token: <see cref="RawToken"/> is shown once to the admin and never
/// stored (only its hash is).</summary>
public sealed record IssuedPasswordSetToken(string RawToken, DateTimeOffset ExpiresAt);

/// <summary>Single-use expiring tokens that let a local user set a password without knowing the
/// old one - backs both "invite user" and admin-generated password reset.</summary>
public interface IPasswordSetTokenStore
{
    /// <summary>Issues a token for <paramref name="userId"/>, replacing any live one.</summary>
    Task<IssuedPasswordSetToken> CreateAsync(Guid userId, PasswordSetPurpose purpose, TimeSpan lifetime, CancellationToken cancellationToken = default);

    /// <summary>Like <see cref="CreateAsync"/>, but returns null (and changes nothing) when the user already
    /// has a token issued less than <paramref name="minInterval"/> ago - the shared, restart-proof throttle
    /// behind self-service forgot-password (ADR-0116).</summary>
    Task<IssuedPasswordSetToken?> TryCreateAsync(Guid userId, PasswordSetPurpose purpose, TimeSpan lifetime, TimeSpan minInterval, CancellationToken cancellationToken = default);

    /// <summary>Atomically consumes <paramref name="rawToken"/>: returns the user it was issued
    /// for and deletes it, or null if unknown/expired/already used.</summary>
    Task<Guid?> ConsumeAsync(string rawToken, CancellationToken cancellationToken = default);
}
