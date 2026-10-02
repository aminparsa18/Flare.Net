using Flare.Cli.Commands;
using Xunit;

namespace Flare.Cli.Tests;

public class TraceDiffTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 2, 10, 0, 0, TimeSpan.Zero);

    private static SpanDtoWire Span(string name, double ms, bool error = false, string service = "api") => new()
    {
        TraceId = "t",
        ServiceName = service,
        Name = name,
        StartTime = T0,
        EndTime = T0.AddMilliseconds(ms),
        DurationNano = (ulong)(ms * 1_000_000),
        StatusCode = error ? "STATUS_CODE_ERROR" : "STATUS_CODE_OK",
    };

    [Fact]
    public void Render_IdenticalTraces_ReportsNoDifferences()
    {
        var output = TraceDiff.Render([Span("GET /x", 100)], [Span("GET /x", 100)]);

        Assert.Contains("No structural, error or significant duration differences.", output);
    }

    [Fact]
    public void Render_AddedAndRemovedSpans_AreListed()
    {
        var output = TraceDiff.Render([Span("GET /x", 100), Span("old", 10)], [Span("GET /x", 100), Span("new", 10)]);

        Assert.Contains("+ api new x1", output);
        Assert.Contains("- api old x1", output);
    }

    [Fact]
    public void Render_DurationChangeAboveThresholds_IsListed()
    {
        var output = TraceDiff.Render([Span("db", 100)], [Span("db", 250)]);

        Assert.Contains("~ api db: 100ms -> 250ms (+150ms)", output);
    }

    [Fact]
    public void Render_SmallDurationChange_IsIgnored()
    {
        // +3ms is under the 5ms absolute floor even though it is +30%.
        var output = TraceDiff.Render([Span("db", 10)], [Span("db", 13)]);

        Assert.Contains("No structural", output);
    }

    [Fact]
    public void Render_ErrorCountChange_IsListed()
    {
        var output = TraceDiff.Render([Span("pay", 50, error: true)], [Span("pay", 50)]);

        Assert.Contains("! api pay: errors 1 -> 0", output);
    }

    [Fact]
    public void Render_SameNameDifferentService_AreDistinct()
    {
        var output = TraceDiff.Render([Span("q", 10, service: "a")], [Span("q", 10, service: "b")]);

        Assert.Contains("+ b q x1", output);
        Assert.Contains("- a q x1", output);
    }
}
