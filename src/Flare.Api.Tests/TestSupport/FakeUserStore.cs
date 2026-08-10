using Flare.Identity.Auth;
using Flare.Identity.Users;

namespace Flare.Api.Tests.TestSupport;

/// <summary>In-memory <see cref="IUserStore"/> - lets <c>AuthEndpoints</c>' handlers be
/// unit-tested with no SQLite/ClickHouse/Redis involved at all, same "pure logic, fake
/// the one interface it depends on" convention as this project's ClickHouse-free query
/// builder tests.</summary>
internal sealed class FakeUserStore : IUserStore
{
    private readonly AspNetPasswordHasher _hasher = new();
    private readonly Dictionary<Guid, (User User, string PasswordHash)> _usersById = [];

    public Task<bool> AnyAsync(CancellationToken cancellationToken = default) => Task.FromResult(_usersById.Count > 0);

    public Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_usersById.TryGetValue(id, out var entry) ? entry.User : null);

    public Task<User?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default) =>
        Task.FromResult((User?)_usersById.Values.SingleOrDefault(e => string.Equals(e.User.Username, username, StringComparison.OrdinalIgnoreCase)).User);

    public Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<User>>(_usersById.Values.Select(e => e.User).ToList());

    public Task<User> CreateAsync(string username, string password, UserRole role, CancellationToken cancellationToken = default)
    {
        if (_usersById.Values.Any(e => string.Equals(e.User.Username, username, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Username '{username}' already exists.");
        }

        var user = new User(Guid.NewGuid(), username, role, DateTimeOffset.UtcNow, IsDisabled: false);
        _usersById[user.Id] = (user, _hasher.HashPassword(password));
        return Task.FromResult(user);
    }

    public Task<User?> VerifyPasswordAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var entry = _usersById.Values.SingleOrDefault(e => string.Equals(e.User.Username, username, StringComparison.OrdinalIgnoreCase));
        if (entry.User is null || entry.User.IsDisabled || !_hasher.VerifyPassword(entry.PasswordHash, password))
        {
            return Task.FromResult<User?>(null);
        }
        return Task.FromResult<User?>(entry.User);
    }

    public Task SetDisabledAsync(Guid id, bool isDisabled, CancellationToken cancellationToken = default)
    {
        if (_usersById.TryGetValue(id, out var entry))
        {
            _usersById[id] = (entry.User with { IsDisabled = isDisabled }, entry.PasswordHash);
        }
        return Task.CompletedTask;
    }

    public Task SetRoleAsync(Guid id, UserRole role, CancellationToken cancellationToken = default)
    {
        if (_usersById.TryGetValue(id, out var entry))
        {
            _usersById[id] = (entry.User with { Role = role }, entry.PasswordHash);
        }
        return Task.CompletedTask;
    }
}
