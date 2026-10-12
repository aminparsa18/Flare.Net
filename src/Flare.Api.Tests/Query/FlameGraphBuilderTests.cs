using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class FlameGraphBuilderTests
{
    private static (IReadOnlyList<string>, long) S(long value, params string[] frames) => (frames, value);

    [Fact]
    public void Build_Empty_YieldsAnEmptyRoot()
    {
        var root = FlameGraphBuilder.Build([]);

        Assert.Equal(FlameGraphBuilder.RootName, root.Name);
        Assert.Equal(0, root.Total);
        Assert.Empty(root.Children);
    }

    [Fact]
    public void Build_MergesSharedPrefixes_AndSumsTotals()
    {
        var root = FlameGraphBuilder.Build([S(10, "main", "a", "x"), S(5, "main", "a", "y"), S(3, "main", "b")]);

        Assert.Equal(18, root.Total);
        var main = Assert.Single(root.Children);
        Assert.Equal(18, main.Total);
        Assert.Equal(0, main.Self);
        Assert.Equal(["a", "b"], main.Children.Select(c => c.Name));
        Assert.Equal(15, main.Children[0].Total);
        Assert.Equal([10L, 5L], main.Children[0].Children.Select(c => c.Total));
    }

    [Fact]
    public void Build_SelfIsTheValueEndingAtThatFrame()
    {
        // A stack that stops at "a" contributes self time to "a" while another passes through it.
        var root = FlameGraphBuilder.Build([S(4, "main", "a"), S(6, "main", "a", "x")]);

        var a = root.Children[0].Children[0];
        Assert.Equal(10, a.Total);
        Assert.Equal(4, a.Self);
        Assert.Equal(6, a.Children[0].Self);
    }

    [Fact]
    public void Build_SortsChildrenHeaviestFirst_TiesByName()
    {
        var root = FlameGraphBuilder.Build([S(1, "b"), S(1, "a"), S(9, "c")]);

        Assert.Equal(["c", "a", "b"], root.Children.Select(c => c.Name));
    }

    [Fact]
    public void Build_IgnoresNonPositiveValues()
    {
        var root = FlameGraphBuilder.Build([S(0, "a"), S(-5, "b")]);

        Assert.Equal(0, root.Total);
        Assert.Empty(root.Children);
    }

    [Fact]
    public void Build_HandlesVeryDeepStacks_WithoutRecursion()
    {
        var frames = Enumerable.Range(0, 50_000).Select(i => $"f{i}").ToArray();

        var root = FlameGraphBuilder.Build([(frames, 1L)]);

        Assert.Equal(1, root.Total);
    }
}
