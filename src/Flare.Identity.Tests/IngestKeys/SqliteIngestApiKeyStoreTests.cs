using Flare.Identity.IngestKeys;
using Flare.Identity.Tests.TestSupport;
using Xunit;

namespace Flare.Identity.Tests.IngestKeys;

public class SqliteIngestApiKeyStoreTests : IAsyncLifetime
{
    private readonly IdentityTestDatabase _database = new();
    private SqliteIngestApiKeyStore _store = null!;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        _store = new SqliteIngestApiKeyStore(_database.ConnectionFactory, TimeProvider.System);
    }

    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task CreateAsync_ReturnsARawKeyThatHashesToTheStoredHash()
    {
        var (key, rawKey) = await _store.CreateAsync("prod-collector");

        var activeHashes = await _store.ListActiveKeyHashesAsync();

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
    public async Task ListActiveKeyHashesAsync_ExcludesRevokedKeys()
    {
        var (key, rawKey) = await _store.CreateAsync("to-be-revoked");

        await _store.RevokeAsync(key.Id);

        var activeHashes = await _store.ListActiveKeyHashesAsync();
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
}
