using Flare.Identity.Auth;

namespace Flare.Api.Tests.TestSupport;

/// <summary>In-memory <see cref="ISessionStore"/> - see <see cref="FakeUserStore"/>'s remarks.</summary>
internal sealed class FakeSessionStore : ISessionStore
{
    private readonly Dictionary<string, Session> _sessionsByToken = [];

    public Task<Session> CreateAsync(Guid userId, TimeSpan lifetime, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var token = Guid.NewGuid().ToString("N");
        var session = new Session(SessionTokenHasher.Hash(token), userId, now, now + lifetime, now, token);
        _sessionsByToken[session.Id] = session with { Token = null };
        return Task.FromResult(session);
    }

    public Task<Session?> FindAsync(string token, CancellationToken cancellationToken = default) =>
        Task.FromResult(_sessionsByToken.TryGetValue(SessionTokenHasher.Hash(token), out var session) && session.ExpiresAt > DateTimeOffset.UtcNow ? session : null);

    public Task DeleteAsync(string token, CancellationToken cancellationToken = default)
    {
        _sessionsByToken.Remove(SessionTokenHasher.Hash(token));
        return Task.CompletedTask;
    }

    public Task DeleteByIdAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _sessionsByToken.Remove(sessionId);
        return Task.CompletedTask;
    }

    public Task DeleteAllForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        foreach (var key in _sessionsByToken.Where(kv => kv.Value.UserId == userId).Select(kv => kv.Key).ToList())
        {
            _sessionsByToken.Remove(key);
        }
        return Task.CompletedTask;
    }

    public Task DeleteAllForUserExceptAsync(Guid userId, string keepToken, CancellationToken cancellationToken = default)
    {
        foreach (var key in _sessionsByToken.Where(kv => kv.Value.UserId == userId && kv.Key != SessionTokenHasher.Hash(keepToken)).Select(kv => kv.Key).ToList())
        {
            _sessionsByToken.Remove(key);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Session>> ListForUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Session>>(
            [.. _sessionsByToken.Values.Where(s => s.UserId == userId && s.ExpiresAt > DateTimeOffset.UtcNow).OrderByDescending(s => s.LastSeenAt)]);

    public Task TouchLastSeenAsync(string token, CancellationToken cancellationToken = default)
    {
        var id = SessionTokenHasher.Hash(token);
        if (_sessionsByToken.TryGetValue(id, out var session))
        {
            _sessionsByToken[id] = session with { LastSeenAt = DateTimeOffset.UtcNow };
        }
        return Task.CompletedTask;
    }

    public bool Contains(string token) => _sessionsByToken.ContainsKey(SessionTokenHasher.Hash(token));
}
