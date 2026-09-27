using Flare.Identity.MetricMetadata;
using Flare.Identity.Tests.TestSupport;
using Xunit;

namespace Flare.Identity.Tests.MetricMetadata;

public class SqliteMetricMetadataOverrideStoreTests : IAsyncLifetime
{
    private readonly IdentityTestDatabase _database = new();
    private SqliteMetricMetadataOverrideStore _store = null!;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        _store = new SqliteMetricMetadataOverrideStore(_database.ConnectionFactory, TimeProvider.System);
    }

    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task GetAllAsync_WithNoOverrides_ReturnsEmpty()
    {
        Assert.Empty(await _store.GetAllAsync());
    }

    [Fact]
    public async Task SetAsync_ThenGetAllAsync_ReturnsTheOverride()
    {
        await _store.SetAsync(new MetricMetadataOverride("http.server.duration", "ms", "Inbound request latency"));

        var entry = Assert.Single(await _store.GetAllAsync());

        Assert.Equal("http.server.duration", entry.Key);
        Assert.Equal(new MetricMetadataOverride("http.server.duration", "ms", "Inbound request latency"), entry.Value);
    }

    [Fact]
    public async Task SetAsync_WithOnlyOneMember_KeepsTheOtherNull()
    {
        await _store.SetAsync(new MetricMetadataOverride("queue.depth", Unit: "{message}", Description: null));

        var stored = (await _store.GetAllAsync())["queue.depth"];

        Assert.Equal("{message}", stored.Unit);
        Assert.Null(stored.Description);
    }

    [Fact]
    public async Task SetAsync_CalledTwice_ReplacesBothMembers()
    {
        await _store.SetAsync(new MetricMetadataOverride("queue.depth", "{message}", "Messages waiting"));
        await _store.SetAsync(new MetricMetadataOverride("queue.depth", Unit: null, Description: "Messages waiting to be consumed"));

        var stored = Assert.Single(await _store.GetAllAsync()).Value;

        Assert.Null(stored.Unit);
        Assert.Equal("Messages waiting to be consumed", stored.Description);
    }

    [Fact]
    public async Task SetAsync_WithOnlyTreatAsCounter_RoundTripsTheFlag()
    {
        await _store.SetAsync(new MetricMetadataOverride("http_requests_total", Unit: null, Description: null, TreatAsCounter: true));

        var stored = (await _store.GetAllAsync())["http_requests_total"];

        Assert.True(stored.TreatAsCounter);
        Assert.Null(stored.Unit);
        Assert.Null(stored.Description);
    }

    [Fact]
    public async Task SetAsync_WithoutTreatAsCounter_ClearsAPreviouslySetFlag()
    {
        await _store.SetAsync(new MetricMetadataOverride("http_requests_total", "{request}", null, TreatAsCounter: true));
        await _store.SetAsync(new MetricMetadataOverride("http_requests_total", "{request}", null));

        Assert.False((await _store.GetAllAsync())["http_requests_total"].TreatAsCounter);
    }

    [Fact]
    public async Task ResetAsync_RemovesOnlyThatMetricsOverride()
    {
        await _store.SetAsync(new MetricMetadataOverride("queue.depth", "{message}", null));
        await _store.SetAsync(new MetricMetadataOverride("http.server.duration", "ms", null));

        await _store.ResetAsync("queue.depth");

        Assert.Equal("http.server.duration", Assert.Single(await _store.GetAllAsync()).Key);
    }

    [Fact]
    public async Task ResetAsync_WithNoExistingOverride_IsANoOp()
    {
        await _store.ResetAsync("queue.depth");

        Assert.Empty(await _store.GetAllAsync());
    }
}
