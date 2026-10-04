using Flare.Identity.PasswordSetTokens;

namespace Flare.Api.Tests.TestSupport;

/// <summary>In-memory <see cref="IPasswordSetTokenStore"/> - see <see cref="FakeUserStore"/>'s remarks.</summary>
internal sealed class FakePasswordSetTokenStore : IPasswordSetTokenStore
{
    private readonly Dictionary<string, (Guid UserId, DateTimeOffset ExpiresAt)> _tokens = [];
    private readonly Dictionary<Guid, DateTimeOffset> _lastCreated = [];

    public async Task<IssuedPasswordSetToken?> TryCreateAsync(Guid userId, PasswordSetPurpose purpose, TimeSpan lifetime, TimeSpan minInterval, CancellationToken cancellationToken = default)
    {
        if (_lastCreated.TryGetValue(userId, out var last) && DateTimeOffset.UtcNow - last < minInterval)
        {
            return null;
        }
        return await CreateAsync(userId, purpose, lifetime, cancellationToken);
    }

    public Task<IssuedPasswordSetToken> CreateAsync(Guid userId, PasswordSetPurpose purpose, TimeSpan lifetime, CancellationToken cancellationToken = default)
    {
        foreach (var key in _tokens.Where(kv => kv.Value.UserId == userId).Select(kv => kv.Key).ToList())
        {
            _tokens.Remove(key);
        }
        _lastCreated[userId] = DateTimeOffset.UtcNow;
        var raw = Guid.NewGuid().ToString("N");
        var expiresAt = DateTimeOffset.UtcNow + lifetime;
        _tokens[raw] = (userId, expiresAt);
        return Task.FromResult(new IssuedPasswordSetToken(raw, expiresAt));
    }

    public Task<Guid?> ConsumeAsync(string rawToken, CancellationToken cancellationToken = default)
    {
        if (_tokens.Remove(rawToken, out var entry) && entry.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return Task.FromResult<Guid?>(entry.UserId);
        }
        return Task.FromResult<Guid?>(null);
    }
}
