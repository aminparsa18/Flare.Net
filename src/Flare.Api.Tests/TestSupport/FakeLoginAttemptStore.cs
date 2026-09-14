using Flare.Identity.Auth;

namespace Flare.Api.Tests.TestSupport;

/// <summary>In-memory <see cref="ILoginAttemptStore"/> - same convention as
/// <see cref="FakeSessionStore"/>. Mirrors <c>SqliteLoginAttemptStore</c>'s policy
/// (window/threshold/lockout-duration) against a provided <see cref="TimeProvider"/> so
/// tests can fast-forward a fake clock instead of sleeping real time.</summary>
internal sealed class FakeLoginAttemptStore(
    TimeProvider timeProvider,
    int maxFailedAttempts = 5,
    TimeSpan? lockoutDuration = null,
    TimeSpan? failureWindow = null) : ILoginAttemptStore
{
    private readonly TimeSpan _lockoutDuration = lockoutDuration ?? TimeSpan.FromMinutes(15);
    private readonly TimeSpan _failureWindow = failureWindow ?? TimeSpan.FromMinutes(15);
    private readonly Dictionary<(string Username, string ClientIp), (int FailedCount, DateTimeOffset LastFailedAt, DateTimeOffset? LockedUntil)> _attempts = [];

    public Task<DateTimeOffset?> GetLockedUntilAsync(string username, string clientIp, CancellationToken cancellationToken = default)
    {
        var key = (username, clientIp);
        if (!_attempts.TryGetValue(key, out var entry) || entry.LockedUntil is not { } lockedUntil)
        {
            // No row, or one still below the failure threshold - leave it alone; see
            // SqliteLoginAttemptStore.GetLockedUntilAsync's remarks for why reaping here
            // would be wrong.
            return Task.FromResult<DateTimeOffset?>(null);
        }

        if (lockedUntil > timeProvider.GetUtcNow())
        {
            return Task.FromResult<DateTimeOffset?>(lockedUntil);
        }

        _attempts.Remove(key);
        return Task.FromResult<DateTimeOffset?>(null);
    }

    public Task RecordFailureAsync(string username, string clientIp, CancellationToken cancellationToken = default)
    {
        var key = (username, clientIp);
        var now = timeProvider.GetUtcNow();
        var failedCount = _attempts.TryGetValue(key, out var existing) && now - existing.LastFailedAt <= _failureWindow
            ? existing.FailedCount + 1
            : 1;
        DateTimeOffset? lockedUntil = failedCount >= maxFailedAttempts ? now + _lockoutDuration : null;
        _attempts[key] = (failedCount, now, lockedUntil);
        return Task.CompletedTask;
    }

    public Task RecordSuccessAsync(string username, string clientIp, CancellationToken cancellationToken = default)
    {
        _attempts.Remove((username, clientIp));
        return Task.CompletedTask;
    }
}
