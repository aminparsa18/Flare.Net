using Flare.Mcp;
using Flare.Cli.Commands;
using Xunit;

namespace Flare.Cli.Tests;

public class RunBoundariesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 2, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Cluster_NoInstances_ReturnsEmpty() =>
        Assert.Empty(RunBoundaries.Cluster([]));

    [Fact]
    public void Cluster_SeparateStarts_AreSeparateRunsNewestFirst()
    {
        var starts = RunBoundaries.Cluster([T0, T0.AddMinutes(10), T0.AddMinutes(5)]);

        Assert.Equal([T0.AddMinutes(10), T0.AddMinutes(5), T0], starts);
    }

    [Fact]
    public void Cluster_ReplicasStartedTogether_CollapseToEarliestMember()
    {
        var starts = RunBoundaries.Cluster([T0.AddSeconds(30), T0, T0.AddSeconds(55)]);

        Assert.Equal([T0], starts);
    }

    [Fact]
    public void Cluster_ChainedWithinWindow_StaysOneRun()
    {
        var starts = RunBoundaries.Cluster([T0, T0.AddSeconds(50), T0.AddSeconds(100)]);

        Assert.Equal([T0], starts);
    }

    [Fact]
    public void Cluster_GapBeyondWindow_SplitsRuns()
    {
        var starts = RunBoundaries.Cluster([T0, T0.AddSeconds(61)]);

        Assert.Equal([T0.AddSeconds(61), T0], starts);
    }
}
