using Flare.Identity.IssueTrackers;
using Flare.Identity.Tests.TestSupport;
using Xunit;

namespace Flare.Identity.Tests.IssueTrackers;

public class DbIssueTrackerStoreTests : IAsyncLifetime
{
    private readonly IdentityTestDatabase _database = new();
    private DbIssueTrackerStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        _store = new DbIssueTrackerStore(_database.ConnectionFactory, TimeProvider.System);
    }

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task GetAllAsync_WithNoRows_ReturnsEmpty() => Assert.Empty(await _store.GetAllAsync(TestContext.Current.CancellationToken));

    [Fact]
    public async Task SetAsync_ThenGetAllAsync_RoundTripsEveryField()
    {
        var config = new IssueTrackerConfig("checkout-api", IssueTrackerProvider.Jira, "https://acme.atlassian.net", "10000", "10001");

        await _store.SetAsync(config, TestContext.Current.CancellationToken);

        Assert.Equal(config, Assert.Single(await _store.GetAllAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task SetAsync_CalledTwiceForTheSameService_Upserts()
    {
        await _store.SetAsync(new IssueTrackerConfig("checkout-api", IssueTrackerProvider.Jira, "https://a.atlassian.net", "1", "2"), TestContext.Current.CancellationToken);
        await _store.SetAsync(new IssueTrackerConfig("checkout-api", IssueTrackerProvider.Jira, "https://b.example/jira", "3", ""), TestContext.Current.CancellationToken);

        var stored = Assert.Single(await _store.GetAllAsync(TestContext.Current.CancellationToken));
        Assert.Equal("https://b.example/jira", stored.SiteUrl);
        Assert.Equal("3", stored.ProjectId);
        Assert.Equal("", stored.IssueTypeId);
    }

    [Fact]
    public async Task DeleteAsync_RemovesOnlyThatService()
    {
        await _store.SetAsync(new IssueTrackerConfig("a", IssueTrackerProvider.Jira, "https://x.atlassian.net", "1", ""), TestContext.Current.CancellationToken);
        await _store.SetAsync(new IssueTrackerConfig("b", IssueTrackerProvider.Jira, "https://x.atlassian.net", "2", ""), TestContext.Current.CancellationToken);

        await _store.DeleteAsync("a", TestContext.Current.CancellationToken);
        await _store.DeleteAsync("missing", TestContext.Current.CancellationToken);

        Assert.Equal("b", Assert.Single(await _store.GetAllAsync(TestContext.Current.CancellationToken)).ServiceName);
    }

    [Fact]
    public async Task SetAsync_RoundTripsAccountEmail_AndApiTokenNullKeepsEmptyClears()
    {
        IssueTrackerConfig With(string? token) => new("a", IssueTrackerProvider.Jira, "https://x.atlassian.net", "1", "2", "bot@acme.test", token);

        await _store.SetAsync(With("secret"), TestContext.Current.CancellationToken);
        await _store.SetAsync(With(null), TestContext.Current.CancellationToken);
        var kept = Assert.Single(await _store.GetAllAsync(TestContext.Current.CancellationToken));
        Assert.Equal("secret", kept.ApiToken);
        Assert.Equal("bot@acme.test", kept.AccountEmail);

        await _store.SetAsync(With(""), TestContext.Current.CancellationToken);
        Assert.Null(Assert.Single(await _store.GetAllAsync(TestContext.Current.CancellationToken)).ApiToken);
    }
}
