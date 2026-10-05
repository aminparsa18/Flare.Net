using Flare.Identity.IngestKeys;
using Flare.Identity.Tests.TestSupport;
using Xunit;

namespace Flare.Identity.Tests.IngestKeys;

public class DbIngestApiKeyStoreTests : IAsyncLifetime
{
    private readonly IdentityTestDatabase _database = new();
    private DbIngestApiKeyStore _store = null!;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        _store = new DbIngestApiKeyStore(_database.ConnectionFactory, TimeProvider.System);
    }

    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task CreateAsync_ReturnsARawKeyThatHashesToTheStoredHash()
    {
        var (key, rawKey) = await _store.CreateAsync("prod-collector");

        var activeHashes = (await _store.ListActiveKeysAsync()).Select(k => k.KeyHash);

        Assert.Contains(IngestApiKeyHasher.Hash(rawKey), activeHashes);
        Assert.Equal("prod-collector", key.Name);
        Assert.True(key.IsActive);
    }

    [Fact]
    public async Task CreateAsync_GeneratesADifferentRawKeyEachTime()
    {
        var (_, rawA) = await _store.CreateAsync("key-a");
        var (_, rawB) = await _store.CreateAsync("key-b");

        Assert.NotEqual(rawA, rawB);
    }

    [Fact]
    public async Task ListActiveKeysAsync_ExcludesRevokedKeys()
    {
        var (key, rawKey) = await _store.CreateAsync("to-be-revoked");

        await _store.RevokeAsync(key.Id);

        var activeHashes = (await _store.ListActiveKeysAsync()).Select(k => k.KeyHash);
        Assert.DoesNotContain(IngestApiKeyHasher.Hash(rawKey), activeHashes);
    }

    [Fact]
    public async Task ListAsync_ReflectsRevocationOnTheRecord()
    {
        var (key, _) = await _store.CreateAsync("to-be-revoked");

        await _store.RevokeAsync(key.Id);

        var found = (await _store.ListAsync()).Single(k => k.Id == key.Id);
        Assert.False(found.IsActive);
        Assert.NotNull(found.RevokedAt);
    }

    [Fact]
    public async Task CreateAsync_StartsWithNoLimits()
    {
        var (key, _) = await _store.CreateAsync("fresh");

        var listed = (await _store.ListAsync()).Single(k => k.Id == key.Id);
        Assert.Equal(IngestApiKeyLimits.None, listed.Limits);
        Assert.False(listed.Limits.IsEnforced);
    }

    [Fact]
    public async Task UpdateLimitsAsync_RoundTripsThroughBothListMethods()
    {
        var (key, _) = await _store.CreateAsync("limited");
        var limits = new IngestApiKeyLimits(Enabled: true, MaxEventsPerMinute: 1_000, MaxBytesPerMinute: null, MaxEventsPerDay: null, MaxBytesPerDay: 5_000_000_000);

        var updated = await _store.UpdateLimitsAsync(key.Id, limits);

        Assert.True(updated);
        Assert.Equal(limits, (await _store.ListAsync()).Single(k => k.Id == key.Id).Limits);
        var active = (await _store.ListActiveKeysAsync()).Single(k => k.Id == key.Id);
        Assert.Equal(limits, active.Limits);
        Assert.Equal("limited", active.Name);
    }

    [Fact]
    public async Task UpdateLimitsAsync_CanClearCapsBackToNull()
    {
        var (key, _) = await _store.CreateAsync("limited");
        await _store.UpdateLimitsAsync(key.Id, new IngestApiKeyLimits(true, 1, 2, 3, 4));

        await _store.UpdateLimitsAsync(key.Id, IngestApiKeyLimits.None);

        Assert.Equal(IngestApiKeyLimits.None, (await _store.ListAsync()).Single(k => k.Id == key.Id).Limits);
    }

    [Fact]
    public async Task UpdateLimitsAsync_ReturnsFalse_ForAnUnknownKey()
    {
        Assert.False(await _store.UpdateLimitsAsync(Guid.NewGuid(), IngestApiKeyLimits.None));
    }
    [Fact]
    public async Task ProjectId_RoundTripsThroughCreateListAndSetProject()
    {
        var project = Guid.NewGuid();
        var (tagged, _) = await _store.CreateAsync("tagged", project);
        var (untagged, _) = await _store.CreateAsync("untagged");

        var keys = await _store.ListAsync();
        Assert.Equal(project, keys.Single(k => k.Id == tagged.Id).ProjectId);
        Assert.Null(keys.Single(k => k.Id == untagged.Id).ProjectId);

        Assert.True(await _store.SetProjectAsync(untagged.Id, project));
        Assert.True(await _store.SetProjectAsync(tagged.Id, null));
        keys = await _store.ListAsync();
        Assert.Equal(project, keys.Single(k => k.Id == untagged.Id).ProjectId);
        Assert.Null(keys.Single(k => k.Id == tagged.Id).ProjectId);
        Assert.False(await _store.SetProjectAsync(Guid.NewGuid(), project));
    }
}
