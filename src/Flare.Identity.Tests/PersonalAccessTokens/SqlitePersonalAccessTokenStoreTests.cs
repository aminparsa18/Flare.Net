using Flare.Identity.Auth;
using Flare.Identity.PersonalAccessTokens;
using Flare.Identity.Tests.TestSupport;
using Flare.Identity.Users;
using Xunit;

namespace Flare.Identity.Tests.PersonalAccessTokens;

public class SqlitePersonalAccessTokenStoreTests : IAsyncLifetime
{
    private readonly IdentityTestDatabase _database = new();
    private readonly ManualTimeProvider _timeProvider = new(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
    private SqlitePersonalAccessTokenStore _store = null!;
    private SqliteUserStore _userStore = null!;
    private Guid _userId;

    // PersonalAccessTokens.UserId REFERENCES Users(Id) (Migrations/0013_personal_access_tokens.sql)
    // and Microsoft.Data.Sqlite enables PRAGMA foreign_keys=ON by default - every token in
    // these tests needs a real Users row behind it, not an arbitrary Guid (same discipline
    // as SqliteSessionStoreTests).
    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        _store = new SqlitePersonalAccessTokenStore(_database.ConnectionFactory, _timeProvider);
        _userStore = new SqliteUserStore(_database.ConnectionFactory, new AspNetPasswordHasher(), TimeProvider.System);
        _userId = (await _userStore.CreateAsync("alice", "correct horse battery staple", UserRole.Viewer)).Id;
    }

    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task CreateAsync_ReturnsARawTokenThatHashesToTheStoredHashAndValidates()
    {
        var (token, rawToken) = await _store.CreateAsync(_userId, "ci-pipeline", expiresAt: null);

        Assert.StartsWith(PersonalAccessTokenHasher.Prefix, rawToken, StringComparison.Ordinal);
        Assert.Equal("ci-pipeline", token.Name);
        Assert.Equal(_userId, token.UserId);
        Assert.True(token.IsActive(_timeProvider.GetUtcNow()));

        var validated = await _store.ValidateAsync(rawToken);
        Assert.NotNull(validated);
        Assert.Equal(token.Id, validated!.Id);
    }

    [Fact]
    public async Task CreateAsync_GeneratesADifferentRawTokenEachTime()
    {
        var (_, rawA) = await _store.CreateAsync(_userId, "token-a", expiresAt: null);
        var (_, rawB) = await _store.CreateAsync(_userId, "token-b", expiresAt: null);

        Assert.NotEqual(rawA, rawB);
    }

    [Fact]
    public async Task ValidateAsync_ReturnsNullForAnUnknownToken()
    {
        var validated = await _store.ValidateAsync(PersonalAccessTokenHasher.GenerateRawToken());

        Assert.Null(validated);
    }

    [Fact]
    public async Task ValidateAsync_ReturnsNullForARevokedToken()
    {
        var (token, rawToken) = await _store.CreateAsync(_userId, "to-be-revoked", expiresAt: null);

        await _store.RevokeAsync(token.Id);

        Assert.Null(await _store.ValidateAsync(rawToken));
    }

    [Fact]
    public async Task ValidateAsync_ReturnsNullForAnExpiredToken()
    {
        var (_, rawToken) = await _store.CreateAsync(_userId, "short-lived", _timeProvider.GetUtcNow().AddDays(1));

        _timeProvider.Advance(TimeSpan.FromDays(2));

        Assert.Null(await _store.ValidateAsync(rawToken));
    }

    [Fact]
    public async Task ValidateAsync_ReturnsATokenThatHasNotYetExpired()
    {
        var (_, rawToken) = await _store.CreateAsync(_userId, "still-valid", _timeProvider.GetUtcNow().AddDays(1));

        _timeProvider.Advance(TimeSpan.FromHours(1));

        Assert.NotNull(await _store.ValidateAsync(rawToken));
    }

    [Fact]
    public async Task ListForUserAsync_OnlyReturnsTokensForThatUser()
    {
        var otherUserId = (await _userStore.CreateAsync("bob", "correct horse battery staple", UserRole.Viewer)).Id;
        await _store.CreateAsync(_userId, "mine", expiresAt: null);
        await _store.CreateAsync(otherUserId, "not-mine", expiresAt: null);

        var mine = await _store.ListForUserAsync(_userId);

        Assert.Single(mine);
        Assert.Equal("mine", mine[0].Name);
    }

    [Fact]
    public async Task ListForUserAsync_ReflectsRevocationOnTheRecord()
    {
        var (token, _) = await _store.CreateAsync(_userId, "to-be-revoked", expiresAt: null);

        await _store.RevokeAsync(token.Id);

        var found = (await _store.ListForUserAsync(_userId)).Single(t => t.Id == token.Id);
        Assert.NotNull(found.RevokedAt);
        Assert.False(found.IsActive(_timeProvider.GetUtcNow()));
    }

    [Fact]
    public async Task FindAsync_ReturnsNullForAnUnknownId()
    {
        Assert.Null(await _store.FindAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task TouchLastUsedAsync_UpdatesLastUsedAt()
    {
        var (token, _) = await _store.CreateAsync(_userId, "touchable", expiresAt: null);
        Assert.Null(token.LastUsedAt);

        _timeProvider.Advance(TimeSpan.FromMinutes(5));
        await _store.TouchLastUsedAsync(token.Id);

        var found = await _store.FindAsync(token.Id);
        Assert.Equal(_timeProvider.GetUtcNow(), found!.LastUsedAt);
    }
}

/// <summary>Minimal advance-able <see cref="TimeProvider"/> for expiry tests - no test
/// project in this repo currently references Microsoft.Extensions.TimeProvider.Testing's
/// FakeTimeProvider, so this stays a plain local override rather than pulling in a new
/// package for one test file's worth of "advance the clock past ExpiresAt" needs.</summary>
internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now += delta;
}
