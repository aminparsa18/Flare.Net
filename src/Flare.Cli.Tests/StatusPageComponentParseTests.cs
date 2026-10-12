using Flare.Cli.Commands;
using Xunit;

namespace Flare.Cli.Tests;

public class StatusPageComponentParseTests
{
    private static readonly Guid Id = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public void Parses_MonitorAndSlo()
    {
        Assert.True(StatusPageFormat.TryParseComponents([$"Website=monitor:{Id}", $"API = SLO:{Id}"], out var components, out _));
        Assert.Collection(components,
            c => { Assert.Equal("Website", c.Name); Assert.Equal("Monitor", c.Kind); Assert.Equal(Id, c.RefId); },
            c => { Assert.Equal("API", c.Name); Assert.Equal("Slo", c.Kind); });
    }

    [Theory]
    [InlineData("nokind")]
    [InlineData("Name=monitor:not-a-guid")]
    [InlineData("Name=host:11111111-2222-3333-4444-555555555555")]
    [InlineData("=monitor:11111111-2222-3333-4444-555555555555")]
    public void Rejects_Malformed(string value)
    {
        Assert.False(StatusPageFormat.TryParseComponents([value], out _, out var error));
        Assert.Contains("--component", error);
    }
}

public class StatusPageSubscriberParseTests
{
    [Fact]
    public void None_StaysNull_SoUpdateLeavesSubscribersAlone()
    {
        Assert.True(StatusPageFormat.TryParseSubscribers(null, out var ids, out _));
        Assert.Null(ids);
    }

    [Fact]
    public void Parses_ChannelIds()
    {
        var id = Guid.NewGuid();
        Assert.True(StatusPageFormat.TryParseSubscribers([id.ToString()], out var ids, out _));
        Assert.Equal([id], ids);
    }

    [Fact]
    public void Rejects_NonGuid()
    {
        Assert.False(StatusPageFormat.TryParseSubscribers(["slack"], out _, out var error));
        Assert.Contains("--subscriber", error);
    }
}

public class StatusIncidentCliTests
{
    [Theory]
    [InlineData("resolved", "Resolved")]
    [InlineData(" MONITORING ", "Monitoring")]
    [InlineData("investigating", "Investigating")]
    [InlineData("done", null)]
    [InlineData(null, null)]
    public void NormalizeStatus_AcceptsAnyCasing(string? input, string? expected) =>
        Assert.Equal(expected, StatusIncidentCli.NormalizeStatus(input));
}
