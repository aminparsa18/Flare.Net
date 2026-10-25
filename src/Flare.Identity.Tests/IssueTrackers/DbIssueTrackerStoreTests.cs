using Flare.Identity.IssueTrackers;
using Flare.Identity.Tests.TestSupport;
using Xunit;

namespace Flare.Identity.Tests.IssueTrackers;

public class DbIssueTrackerStoreTests : IAsyncLifetime
{
    private readonly IdentityTestDatabase _database = new();
    private DbIssueTrackerStore _store = null!;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        _store = new DbIssueTrackerStore(_database.ConnectionFactory, TimeProvider.System);
    }

    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task GetAllAsync_WithNoRows_ReturnsEmpty() => Assert.Empty(await _store.GetAllAsync());

    [Fact]
    public async Task SetAsync_ThenGetAllAsync_RoundTripsEveryField()
    {
        var config = new IssueTrackerConfig("checkout-api", IssueTrackerProvider.Jira, "https://acme.atlassian.net", "10000", "10001");

        await _store.SetAsync(config);

        Assert.Equal(config, Assert.Single(await _store.GetAllAsync()));
    }

    [Fact]
    public async Task SetAsync_CalledTwiceForTheSameService_Upserts()
    {
        await _store.SetAsync(new IssueTrackerConfig("checkout-api", IssueTrackerProvider.Jira, "https://a.atlassian.net", "1", "2"));
        await _store.SetAsync(new IssueTrackerConfig("checkout-api", IssueTrackerProvider.Jira, "https://b.example/jira", "3", ""));

        var stored = Assert.Single(await _store.GetAllAsync());
        Assert.Equal("https://b.example/jira", stored.SiteUrl);
        Assert.Equal("3", stored.ProjectId);
        Assert.Equal("", stored.IssueTypeId);
    }

    [Fact]
    public async Task DeleteAsync_RemovesOnlyThatService()
    {
        await _store.SetAsync(new IssueTrackerConfig("a", IssueTrackerProvider.Jira, "https://x.atlassian.net", "1", ""));
        await _store.SetAsync(new IssueTrackerConfig("b", IssueTrackerProvider.Jira, "https://x.atlassian.net", "2", ""));

        await _store.DeleteAsync("a");
        await _store.DeleteAsync("missing");

        Assert.Equal("b", Assert.Single(await _store.GetAllAsync()).ServiceName);
    }
}
