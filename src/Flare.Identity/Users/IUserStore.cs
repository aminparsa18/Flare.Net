namespace Flare.Identity.Users;

public interface IUserStore
{
    /// <summary>True once at least one user exists - the dashboard's <c>/setup</c>
    /// bootstrap flow shows the "create admin" form only while this is false.</summary>
    Task<bool> AnyAsync(CancellationToken cancellationToken = default);

    Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<User?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken = default);

    Task<User> CreateAsync(string username, string password, UserRole role, CancellationToken cancellationToken = default);

    /// <summary>Looks up <paramref name="username"/> and verifies <paramref name="password"/>
    /// against its stored hash in one call. Returns null on any failure (unknown username,
    /// wrong password, or a disabled account) - callers must not distinguish these cases
    /// in the response, to avoid leaking whether a username exists.</summary>
    Task<User?> VerifyPasswordAsync(string username, string password, CancellationToken cancellationToken = default);

    Task SetDisabledAsync(Guid id, bool isDisabled, CancellationToken cancellationToken = default);

    Task SetRoleAsync(Guid id, UserRole role, CancellationToken cancellationToken = default);
}
