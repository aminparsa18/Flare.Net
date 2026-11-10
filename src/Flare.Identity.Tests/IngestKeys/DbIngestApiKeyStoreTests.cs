using Flare.Identity.IngestKeys;
using Flare.Identity.Tests.TestSupport;
using Xunit;

namespace Flare.Identity.Tests.IngestKeys;

public class DbIngestApiKeyStoreTests : IAsyncLifetime
{
    private readonly IdentityTestDatabase _database = new();
    private DbIngestApiKeyStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        _store = new DbIngestApiKeyStore(_database.ConnectionFactory, TimeProvider.System);
    }

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task CreateAsync_ReturnsARawKeyThatHashesToTheStoredHash()
    {
        var (key, rawKey) = await _store.CreateAsync("prod-collector", cancellationToken: TestContext.Current.CancellationToken);

        var activeHashes = (await _store.ListActiveKeysAsync(TestContext.Current.CancellationToken)).Select(k => k.KeyHash);

        Assert.Contains(IngestApiKeyHasher.Hash(rawKey), activeHashes);
        Assert.Equal("prod-collector", key.Name);
        Assert.True(key.IsActive);
    }

    [Fact]
    public async Task SetAllowedOriginsAsync_RoundTripsThroughListAndActiveKeys_AndClears()
    {
        var (key, _) = await _store.CreateAsync("browser", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty((await _store.ListAsync(TestContext.Current.CancellationToken)).Single().AllowedOrigins);

        Assert.True(await _store.SetAllowedOriginsAsync(key.Id, ["https://a.example.com", "http://localhost:5173"], TestContext.Current.CancellationToken));

        Assert.Equal(["https://a.example.com", "http://localhost:5173"], (await _store.ListAsync(TestContext.Current.CancellationToken)).Single().AllowedOrigins);
        Assert.Equal(["https://a.example.com", "http://localhost:5173"], (await _store.ListActiveKeysAsync(TestContext.Current.CancellationToken)).Single().AllowedOrigins);

        await _store.SetAllowedOriginsAsync(key.Id, [], TestContext.Current.CancellationToken);
        Assert.Empty((await _store.ListAsync(TestContext.Current.CancellationToken)).Single().AllowedOrigins);
        Assert.False(await _store.SetAllowedOriginsAsync(Guid.NewGuid(), [], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Expiry_RoundTripsThroughCreateListAndActiveKeys_SetsAndClears()
    {
        var at = DateTimeOffset.UtcNow.AddDays(30);
        var (key, _) = await _store.CreateAsync("short-lived", expiresAt: at, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(at, (await _store.ListAsync(TestContext.Current.CancellationToken)).Single().ExpiresAt);
        Assert.Equal(at, (await _store.ListActiveKeysAsync(TestContext.Current.CancellationToken)).Single().ExpiresAt);

        Assert.True(await _store.SetExpiryAsync(key.Id, null, TestContext.Current.CancellationToken));
        Assert.Null((await _store.ListAsync(TestContext.Current.CancellationToken)).Single().ExpiresAt);
        Assert.False(await _store.SetExpiryAsync(Guid.NewGuid(), at, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SetAllowedServicesAsync_RoundTripsThroughListAndActiveKeys_AndClears()
    {
        var (key, _) = await _store.CreateAsync("browser", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(await _store.SetAllowedServicesAsync(key.Id, ["web", "checkout"], TestContext.Current.CancellationToken));

        Assert.Equal(["web", "checkout"], (await _store.ListAsync(TestContext.Current.CancellationToken)).Single().AllowedServices);
        Assert.Equal(["web", "checkout"], (await _store.ListActiveKeysAsync(TestContext.Current.CancellationToken)).Single().AllowedServices);

        await _store.SetAllowedServicesAsync(key.Id, [], TestContext.Current.CancellationToken);
        Assert.Empty((await _store.ListAsync(TestContext.Current.CancellationToken)).Single().AllowedServices);
        Assert.False(await _store.SetAllowedServicesAsync(Guid.NewGuid(), [], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateAsync_GeneratesADifferentRawKeyEachTime()
    {
        var (_, rawA) = await _store.CreateAsync("key-a", cancellationToken: TestContext.Current.CancellationToken);
        var (_, rawB) = await _store.CreateAsync("key-b", cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEqual(rawA, rawB);
    }

    [Fact]
    public async Task ListActiveKeysAsync_ExcludesRevokedKeys()
    {
        var (key, rawKey) = await _store.CreateAsync("to-be-revoked", cancellationToken: TestContext.Current.CancellationToken);

        await _store.RevokeAsync(key.Id, TestContext.Current.CancellationToken);

        var activeHashes = (await _store.ListActiveKeysAsync(TestContext.Current.CancellationToken)).Select(k => k.KeyHash);
        Assert.DoesNotContain(IngestApiKeyHasher.Hash(rawKey), activeHashes);
    }

    [Fact]
    public async Task ListAsync_ReflectsRevocationOnTheRecord()
    {
        var (key, _) = await _store.CreateAsync("to-be-revoked", cancellationToken: TestContext.Current.CancellationToken);

        await _store.RevokeAsync(key.Id, TestContext.Current.CancellationToken);

        var found = (await _store.ListAsync(TestContext.Current.CancellationToken)).Single(k => k.Id == key.Id);
        Assert.False(found.IsActive);
        Assert.NotNull(found.RevokedAt);
    }

    [Fact]
    public async Task CreateAsync_StartsWithNoLimits()
    {
        var (key, _) = await _store.CreateAsync("fresh", cancellationToken: TestContext.Current.CancellationToken);

        var listed = (await _store.ListAsync(TestContext.Current.CancellationToken)).Single(k => k.Id == key.Id);
        Assert.Equal(IngestApiKeyLimits.None, listed.Limits);
        Assert.False(listed.Limits.IsEnforced);
    }

    [Fact]
    public async Task UpdateLimitsAsync_RoundTripsTheReplayCaps()
    {
        var (key, _) = await _store.CreateAsync("replay-limited", cancellationToken: TestContext.Current.CancellationToken);
        var limits = new IngestApiKeyLimits(true, null, null, null, null, MaxReplayBytesPerMinute: 50_000_000, MaxReplayBytesPerDay: 2_000_000_000);

        await _store.UpdateLimitsAsync(key.Id, limits, TestContext.Current.CancellationToken);

        Assert.Equal(limits, (await _store.ListAsync(TestContext.Current.CancellationToken)).Single(k => k.Id == key.Id).Limits);
        Assert.Equal(limits, (await _store.ListActiveKeysAsync(TestContext.Current.CancellationToken)).Single(k => k.Id == key.Id).Limits);
    }

    [Fact]
    public async Task UpdateLimitsAsync_RoundTripsThroughBothListMethods()
    {
        var (key, _) = await _store.CreateAsync("limited", cancellationToken: TestContext.Current.CancellationToken);
        var limits = new IngestApiKeyLimits(Enabled: true, MaxEventsPerMinute: 1_000, MaxBytesPerMinute: null, MaxEventsPerDay: null, MaxBytesPerDay: 5_000_000_000);

        var updated = await _store.UpdateLimitsAsync(key.Id, limits, TestContext.Current.CancellationToken);

        Assert.True(updated);
        Assert.Equal(limits, (await _store.ListAsync(TestContext.Current.CancellationToken)).Single(k => k.Id == key.Id).Limits);
        var active = (await _store.ListActiveKeysAsync(TestContext.Current.CancellationToken)).Single(k => k.Id == key.Id);
        Assert.Equal(limits, active.Limits);
        Assert.Equal("limited", active.Name);
    }

    [Fact]
    public async Task UpdateLimitsAsync_CanClearCapsBackToNull()
    {
        var (key, _) = await _store.CreateAsync("limited", cancellationToken: TestContext.Current.CancellationToken);
        await _store.UpdateLimitsAsync(key.Id, new IngestApiKeyLimits(true, 1, 2, 3, 4), TestContext.Current.CancellationToken);

        await _store.UpdateLimitsAsync(key.Id, IngestApiKeyLimits.None, TestContext.Current.CancellationToken);

        Assert.Equal(IngestApiKeyLimits.None, (await _store.ListAsync(TestContext.Current.CancellationToken)).Single(k => k.Id == key.Id).Limits);
    }

    [Fact]
    public async Task UpdateLimitsAsync_ReturnsFalse_ForAnUnknownKey()
    {
        Assert.False(await _store.UpdateLimitsAsync(Guid.NewGuid(), IngestApiKeyLimits.None, TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task ProjectId_RoundTripsThroughCreateListAndSetProject()
    {
        var project = Guid.NewGuid();
        var (tagged, _) = await _store.CreateAsync("tagged", project, cancellationToken: TestContext.Current.CancellationToken);
        var (untagged, _) = await _store.CreateAsync("untagged", cancellationToken: TestContext.Current.CancellationToken);

        var keys = await _store.ListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(project, keys.Single(k => k.Id == tagged.Id).ProjectId);
        Assert.Null(keys.Single(k => k.Id == untagged.Id).ProjectId);

        Assert.True(await _store.SetProjectAsync(untagged.Id, project, TestContext.Current.CancellationToken));
        Assert.True(await _store.SetProjectAsync(tagged.Id, null, TestContext.Current.CancellationToken));
        keys = await _store.ListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(project, keys.Single(k => k.Id == untagged.Id).ProjectId);
        Assert.Null(keys.Single(k => k.Id == tagged.Id).ProjectId);
        Assert.False(await _store.SetProjectAsync(Guid.NewGuid(), project, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RenameAsync_ChangesTheName_AndReportsMissingKeys()
    {
        var (key, _) = await _store.CreateAsync("old-name", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(await _store.RenameAsync(key.Id, "new-name", TestContext.Current.CancellationToken));
        Assert.Equal("new-name", Assert.Single(await _store.ListAsync(TestContext.Current.CancellationToken)).Name);
        Assert.False(await _store.RenameAsync(Guid.NewGuid(), "x", TestContext.Current.CancellationToken));
    }
}
