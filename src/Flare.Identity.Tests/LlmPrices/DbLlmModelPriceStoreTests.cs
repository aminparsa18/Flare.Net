using Flare.Identity.LlmPrices;
using Flare.Identity.Tests.TestSupport;
using Xunit;

namespace Flare.Identity.Tests.LlmPrices;

public class DbLlmModelPriceStoreTests : IAsyncLifetime
{
    private readonly IdentityTestDatabase _database = new();
    private DbLlmModelPriceStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        _store = new DbLlmModelPriceStore(_database.ConnectionFactory, TimeProvider.System);
    }

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task GetAllAsync_WithNoOverrides_ReturnsEmpty()
    {
        Assert.Empty(await _store.GetAllAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SetAsync_ThenGetAllAsync_ReturnsThePrice_MatchedCaseInsensitively()
    {
        await _store.SetAsync(new LlmModelPrice("GPT-4o", 2.5, 10), TestContext.Current.CancellationToken);

        var all = await _store.GetAllAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new LlmModelPrice("GPT-4o", 2.5, 10), all["gpt-4o"]);
    }

    [Fact]
    public async Task SetAsync_CalledTwiceForTheSameModel_ReplacesThePrice()
    {
        await _store.SetAsync(new LlmModelPrice("gpt-4o", 1, 2), TestContext.Current.CancellationToken);
        await _store.SetAsync(new LlmModelPrice("GPT-4O", 3, 4), TestContext.Current.CancellationToken);

        var entry = Assert.Single(await _store.GetAllAsync(TestContext.Current.CancellationToken));

        Assert.Equal(3, entry.Value.InputPerMillion);
        Assert.Equal(4, entry.Value.OutputPerMillion);
    }

    [Fact]
    public async Task ResetAsync_RemovesOnlyThatModel_AndIsANoOpWhenAbsent()
    {
        await _store.SetAsync(new LlmModelPrice("a", 1, 1), TestContext.Current.CancellationToken);
        await _store.SetAsync(new LlmModelPrice("b", 2, 2), TestContext.Current.CancellationToken);

        await _store.ResetAsync("A", TestContext.Current.CancellationToken);
        await _store.ResetAsync("never-set", TestContext.Current.CancellationToken);

        Assert.Equal(["b"], (await _store.GetAllAsync(TestContext.Current.CancellationToken)).Keys);
    }
}
