namespace Flare.Identity.Users;

public interface IUserStore
{
    /// <summary>True once at least one user exists - the dashboard's <c>/setup</c>
    /// bootstrap flow shows the "create admin" form only while this is false.</summary>
    Task<bool> AnyAsync(CancellationToken cancellationToken = default);

    Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<User?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default);

    /// <summary>Looks up an Entra-provisioned account by its external identity
    /// (<paramref name="authProvider"/> is always "Entra" today, but takes the value
    /// rather than a hardcoded literal so a second external provider wouldn't need a
    /// second method). Null on first-ever login for that identity - the caller then
    /// provisions via <see cref="CreateFromExternalAsync"/>.</summary>
    Task<User?> FindByExternalIdAsync(string authProvider, string externalId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken = default);

    Task<User> CreateAsync(string username, string password, UserRole role, CancellationToken cancellationToken = default);

    /// <summary>First-login provisioning for an SSO account. <paramref name="role"/> is
    /// only ever consulted here - once created, an Entra-provisioned user's role lives in
    /// this table exactly like a local user's, managed via <see cref="SetRoleAsync"/>, not
    /// re-derived from the identity provider on subsequent logins (see docs/auth.md).
    /// Gets a real, well-formed password hash generated from a random, never-revealed
    /// string (not a hand-rolled sentinel) - it always fails <see cref="VerifyPasswordAsync"/>
    /// against any real guess without needing the hasher to special-case a malformed value.</summary>
    Task<User> CreateFromExternalAsync(string authProvider, string externalId, string username, UserRole role, CancellationToken cancellationToken = default);

    /// <summary>Looks up <paramref name="username"/> and verifies <paramref name="password"/>
    /// against its stored hash in one call. Returns null on any failure (unknown username,
    /// wrong password, or a disabled account) - callers must not distinguish these cases
    /// in the response, to avoid leaking whether a username exists.</summary>
    Task<User?> VerifyPasswordAsync(string username, string password, CancellationToken cancellationToken = default);

    Task SetDisabledAsync(Guid id, bool isDisabled, CancellationToken cancellationToken = default);

    Task SetRoleAsync(Guid id, UserRole role, CancellationToken cancellationToken = default);
}
