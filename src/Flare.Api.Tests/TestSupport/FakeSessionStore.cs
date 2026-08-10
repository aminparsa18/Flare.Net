using Flare.Identity.Auth;

namespace Flare.Api.Tests.TestSupport;

/// <summary>In-memory <see cref="ISessionStore"/> - see <see cref="FakeUserStore"/>'s remarks.</summary>
internal sealed class FakeSessionStore : ISessionStore
{
    private readonly Dictionary<string, Session> _sessionsByToken = [];

    public Task<Session> CreateAsync(Guid userId, TimeSpan lifetime, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var session = new Session(Guid.NewGuid().ToString("N"), userId, now, now + lifetime, now);
        _sessionsByToken[session.Id] = session;
        return Task.FromResult(session);
    }

    public Task<Session?> FindAsync(string token, CancellationToken cancellationToken = default) =>
        Task.FromResult(_sessionsByToken.TryGetValue(token, out var session) && session.ExpiresAt > DateTimeOffset.UtcNow ? session : null);

    public Task DeleteAsync(string token, CancellationToken cancellationToken = default)
    {
        _sessionsByToken.Remove(token);
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

    public Task TouchLastSeenAsync(string token, CancellationToken cancellationToken = default)
    {
        if (_sessionsByToken.TryGetValue(token, out var session))
        {
            _sessionsByToken[token] = session with { LastSeenAt = DateTimeOffset.UtcNow };
        }
        return Task.CompletedTask;
    }

    public bool Contains(string token) => _sessionsByToken.ContainsKey(token);
}
