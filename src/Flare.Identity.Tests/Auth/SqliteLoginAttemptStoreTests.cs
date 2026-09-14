using Flare.Identity.Auth;
using Flare.Identity.Tests.PersonalAccessTokens;
using Flare.Identity.Tests.TestSupport;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flare.Identity.Tests.Auth;

public class SqliteLoginAttemptStoreTests : IAsyncLifetime
{
    private readonly IdentityTestDatabase _database = new();
    private readonly ManualTimeProvider _timeProvider = new(DateTimeOffset.UtcNow);
    private SqliteLoginAttemptStore _store = null!;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        _store = new SqliteLoginAttemptStore(_database.ConnectionFactory, _timeProvider, Options.Create(new AuthOptions
        {
            MaxFailedLoginAttempts = 3,
            LoginLockoutDuration = TimeSpan.FromMinutes(15),
            LoginFailureWindow = TimeSpan.FromMinutes(15),
        }));
    }

    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task GetLockedUntilAsync_ReturnsNull_ForAPairThatHasNeverFailed()
    {
        Assert.Null(await _store.GetLockedUntilAsync("alice", "127.0.0.1"));
    }

    [Fact]
    public async Task GetLockedUntilAsync_ReturnsNull_WhileBelowTheFailureThreshold()
    {
        await _store.RecordFailureAsync("alice", "127.0.0.1");
        await _store.RecordFailureAsync("alice", "127.0.0.1");

        Assert.Null(await _store.GetLockedUntilAsync("alice", "127.0.0.1"));
    }

    [Fact]
    public async Task GetLockedUntilAsync_ReturnsALockout_OnceTheThresholdIsReached()
    {
        await _store.RecordFailureAsync("alice", "127.0.0.1");
        await _store.RecordFailureAsync("alice", "127.0.0.1");
        await _store.RecordFailureAsync("alice", "127.0.0.1");

        var lockedUntil = await _store.GetLockedUntilAsync("alice", "127.0.0.1");

        Assert.NotNull(lockedUntil);
        Assert.Equal(_timeProvider.GetUtcNow() + TimeSpan.FromMinutes(15), lockedUntil);
    }

    [Fact]
    public async Task GetLockedUntilAsync_ReturnsNull_OnceTheLockoutExpires()
    {
        await _store.RecordFailureAsync("alice", "127.0.0.1");
        await _store.RecordFailureAsync("alice", "127.0.0.1");
        await _store.RecordFailureAsync("alice", "127.0.0.1");
        Assert.NotNull(await _store.GetLockedUntilAsync("alice", "127.0.0.1"));

        _timeProvider.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(1));

        Assert.Null(await _store.GetLockedUntilAsync("alice", "127.0.0.1"));
    }

    [Fact]
    public async Task RecordFailureAsync_ResetsTheStreak_WhenThePriorFailureIsOutsideTheWindow()
    {
        await _store.RecordFailureAsync("alice", "127.0.0.1");
        await _store.RecordFailureAsync("alice", "127.0.0.1");

        _timeProvider.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(1));

        // A 3rd failure, but the first two are now outside the window - should count as
        // failure #1 of a fresh streak, not #3, so no lockout yet.
        await _store.RecordFailureAsync("alice", "127.0.0.1");

        Assert.Null(await _store.GetLockedUntilAsync("alice", "127.0.0.1"));
    }

    [Fact]
    public async Task RecordSuccessAsync_ClearsAnyTrackedFailures()
    {
        await _store.RecordFailureAsync("alice", "127.0.0.1");
        await _store.RecordFailureAsync("alice", "127.0.0.1");

        await _store.RecordSuccessAsync("alice", "127.0.0.1");
        await _store.RecordFailureAsync("alice", "127.0.0.1");

        // Only 1 failure since the reset - well below the threshold of 3.
        Assert.Null(await _store.GetLockedUntilAsync("alice", "127.0.0.1"));
    }

    [Fact]
    public async Task FailuresAreScoped_ToTheUsernameAndClientIpPairTogether()
    {
        await _store.RecordFailureAsync("alice", "10.0.0.1");
        await _store.RecordFailureAsync("alice", "10.0.0.1");
        await _store.RecordFailureAsync("alice", "10.0.0.1");
        Assert.NotNull(await _store.GetLockedUntilAsync("alice", "10.0.0.1"));

        // Same username, different IP - unaffected by the lockout above.
        Assert.Null(await _store.GetLockedUntilAsync("alice", "10.0.0.2"));

        // Same IP, different username - also unaffected.
        Assert.Null(await _store.GetLockedUntilAsync("bob", "10.0.0.1"));
    }
}
