using Flare.Identity.PasswordSetTokens;

namespace Flare.Api.Tests.TestSupport;

/// <summary>In-memory <see cref="IPasswordSetTokenStore"/> - see <see cref="FakeUserStore"/>'s remarks.</summary>
internal sealed class FakePasswordSetTokenStore : IPasswordSetTokenStore
{
    private readonly Dictionary<string, (Guid UserId, DateTimeOffset ExpiresAt)> _tokens = [];

    public Task<IssuedPasswordSetToken> CreateAsync(Guid userId, PasswordSetPurpose purpose, TimeSpan lifetime, CancellationToken cancellationToken = default)
    {
        foreach (var key in _tokens.Where(kv => kv.Value.UserId == userId).Select(kv => kv.Key).ToList())
        {
            _tokens.Remove(key);
        }
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
