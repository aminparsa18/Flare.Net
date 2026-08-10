using Flare.Ingest.Auth;
using Flare.Ingest.Tests.Auth.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flare.Ingest.Tests.Auth;

public class IngestApiKeyCacheTests
{
    [Fact]
    public void IsValid_ReturnsFalse_BeforeInitializeAsyncHasRun()
    {
        var cache = CreateCache(new FakeIngestApiKeyStore(), new IngestAuthOptions());

        Assert.False(cache.IsValid("anything"));
    }

    [Fact]
    public async Task IsValid_ReturnsTrue_ForAKeyCreatedInTheStore_AfterInitializeAsync()
    {
        var store = new FakeIngestApiKeyStore();
        var (_, rawKey) = await store.CreateAsync("prod-collector");
        var cache = CreateCache(store, new IngestAuthOptions());

        await cache.InitializeAsync(CancellationToken.None);

        Assert.True(cache.IsValid(rawKey));
        Assert.False(cache.IsValid("some-other-key"));
    }

    [Fact]
    public async Task IsValid_ReturnsFalse_ForARevokedKey()
    {
        var store = new FakeIngestApiKeyStore();
        var (key, rawKey) = await store.CreateAsync("prod-collector");
        await store.RevokeAsync(key.Id);
        var cache = CreateCache(store, new IngestAuthOptions());

        await cache.InitializeAsync(CancellationToken.None);

        Assert.False(cache.IsValid(rawKey));
    }

    [Fact]
    public async Task IsValid_ReturnsTrue_ForTheStaticIngestApiKey_EvenWithNoSqliteBackedKeys()
    {
        var cache = CreateCache(new FakeIngestApiKeyStore(), new IngestAuthOptions { StaticIngestApiKey = "fixed-key-123" });

        await cache.InitializeAsync(CancellationToken.None);

        Assert.True(cache.IsValid("fixed-key-123"));
        Assert.False(cache.IsValid("wrong-key"));
    }

    [Fact]
    public async Task IsValid_AcceptsBothAStoreKeyAndTheStaticKeyTogether()
    {
        var store = new FakeIngestApiKeyStore();
        var (_, rawKey) = await store.CreateAsync("prod-collector");
        var cache = CreateCache(store, new IngestAuthOptions { StaticIngestApiKey = "fixed-key-123" });

        await cache.InitializeAsync(CancellationToken.None);

        Assert.True(cache.IsValid(rawKey));
        Assert.True(cache.IsValid("fixed-key-123"));
    }

    private static IngestApiKeyCache CreateCache(FakeIngestApiKeyStore store, IngestAuthOptions options) =>
        new(store, Options.Create(options), NullLogger<IngestApiKeyCache>.Instance);
}
