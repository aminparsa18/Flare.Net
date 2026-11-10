using Flare.Identity.MetricMetadata;
using Flare.Identity.Tests.TestSupport;
using Xunit;

namespace Flare.Identity.Tests.MetricMetadata;

public class DbMetricMetadataOverrideStoreTests : IAsyncLifetime
{
    private readonly IdentityTestDatabase _database = new();
    private DbMetricMetadataOverrideStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        _store = new DbMetricMetadataOverrideStore(_database.ConnectionFactory, TimeProvider.System);
    }

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task GetAllAsync_WithNoOverrides_ReturnsEmpty()
    {
        Assert.Empty(await _store.GetAllAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SetAsync_ThenGetAllAsync_ReturnsTheOverride()
    {
        await _store.SetAsync(new MetricMetadataOverride("http.server.duration", "ms", "Inbound request latency"), TestContext.Current.CancellationToken);

        var entry = Assert.Single(await _store.GetAllAsync(TestContext.Current.CancellationToken));

        Assert.Equal("http.server.duration", entry.Key);
        Assert.Equal(new MetricMetadataOverride("http.server.duration", "ms", "Inbound request latency"), entry.Value);
    }

    [Fact]
    public async Task SetAsync_WithOnlyOneMember_KeepsTheOtherNull()
    {
        await _store.SetAsync(new MetricMetadataOverride("queue.depth", Unit: "{message}", Description: null), TestContext.Current.CancellationToken);

        var stored = (await _store.GetAllAsync(TestContext.Current.CancellationToken))["queue.depth"];

        Assert.Equal("{message}", stored.Unit);
        Assert.Null(stored.Description);
    }

    [Fact]
    public async Task SetAsync_CalledTwice_ReplacesBothMembers()
    {
        await _store.SetAsync(new MetricMetadataOverride("queue.depth", "{message}", "Messages waiting"), TestContext.Current.CancellationToken);
        await _store.SetAsync(new MetricMetadataOverride("queue.depth", Unit: null, Description: "Messages waiting to be consumed"), TestContext.Current.CancellationToken);

        var stored = Assert.Single(await _store.GetAllAsync(TestContext.Current.CancellationToken)).Value;

        Assert.Null(stored.Unit);
        Assert.Equal("Messages waiting to be consumed", stored.Description);
    }

    [Fact]
    public async Task SetAsync_WithOnlyTreatAsCounter_RoundTripsTheFlag()
    {
        await _store.SetAsync(new MetricMetadataOverride("http_requests_total", Unit: null, Description: null, TreatAsCounter: true), TestContext.Current.CancellationToken);

        var stored = (await _store.GetAllAsync(TestContext.Current.CancellationToken))["http_requests_total"];

        Assert.True(stored.TreatAsCounter);
        Assert.Null(stored.Unit);
        Assert.Null(stored.Description);
    }

    [Fact]
    public async Task SetAsync_WithoutTreatAsCounter_ClearsAPreviouslySetFlag()
    {
        await _store.SetAsync(new MetricMetadataOverride("http_requests_total", "{request}", null, TreatAsCounter: true), TestContext.Current.CancellationToken);
        await _store.SetAsync(new MetricMetadataOverride("http_requests_total", "{request}", null), TestContext.Current.CancellationToken);

        Assert.False((await _store.GetAllAsync(TestContext.Current.CancellationToken))["http_requests_total"].TreatAsCounter);
    }

    [Fact]
    public async Task ResetAsync_RemovesOnlyThatMetricsOverride()
    {
        await _store.SetAsync(new MetricMetadataOverride("queue.depth", "{message}", null), TestContext.Current.CancellationToken);
        await _store.SetAsync(new MetricMetadataOverride("http.server.duration", "ms", null), TestContext.Current.CancellationToken);

        await _store.ResetAsync("queue.depth", TestContext.Current.CancellationToken);

        Assert.Equal("http.server.duration", Assert.Single(await _store.GetAllAsync(TestContext.Current.CancellationToken)).Key);
    }

    [Fact]
    public async Task ResetAsync_WithNoExistingOverride_IsANoOp()
    {
        await _store.ResetAsync("queue.depth", TestContext.Current.CancellationToken);

        Assert.Empty(await _store.GetAllAsync(TestContext.Current.CancellationToken));
    }
}
