using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class MetricRelatedRankerTests
{
    private static MetricRelationCandidate Metric(string name, string[]? services = null, string[]? keys = null, MetricPointType type = MetricPointType.Gauge) =>
        new(name, type, services ?? [], keys ?? []);

    [Theory]
    [InlineData("http.server.request.duration", "http.server.active_requests", "http.server", 2)]
    [InlineData("http.server.request.duration", "http.client.request.duration", "http", 1)]
    [InlineData("http.server.duration", "http.service.duration", "http", 1)]
    [InlineData("process_cpu_seconds_total", "process_resident_memory_bytes", "process", 1)]
    [InlineData("db.client.operation.duration", "http.client.request.duration", null, 0)]
    [InlineData("kestrel", "kestrel.connections", "kestrel", 1)]
    [InlineData("jvm.memory.used", "jvm.memory.used", "jvm.memory.used", 3)]
    [InlineData("a.b", "a_b", "a", 1)]
    public void SharedPrefix_CountsWholeLeadingSegmentsOnly(string a, string b, string? prefix, int segments)
    {
        Assert.Equal((prefix, segments), MetricRelatedRanker.SharedPrefix(a, b));
    }

    [Fact]
    public void Rank_ExcludesTheTargetItself_ButNotTheSameNameUnderAnotherType()
    {
        var target = Metric("orders.processed", keys: ["region"], type: MetricPointType.Sum);
        var related = MetricRelatedRanker.Rank(
            target,
            [target, Metric("orders.processed", keys: ["region"], type: MetricPointType.Gauge)],
            10);

        var only = Assert.Single(related);
        Assert.Equal(MetricPointType.Gauge, only.Type);
    }

    [Fact]
    public void Rank_SharingOnlyAService_DoesNotQualify()
    {
        var target = Metric("orders.processed", services: ["checkout"], keys: ["region"]);
        var related = MetricRelatedRanker.Rank(target, [Metric("cache.hits", services: ["checkout"], keys: ["cache"])], 10);

        Assert.Empty(related);
    }

    [Fact]
    public void Rank_OrdersByPrefixThenKeysThenServices_ReportingTheEvidence()
    {
        var target = Metric("http.server.request.duration", services: ["api", "web"], keys: ["http.route", "http.request.method"]);
        var related = MetricRelatedRanker.Rank(
            target,
            [
                Metric("dotnet.gc.collections", services: ["api"], keys: ["http.route"]),                    // 0 + 2 + 1 = 3
                Metric("http.server.active_requests", services: ["api"], keys: ["http.request.method"]),     // 6 + 2 + 1 = 9
                Metric("http.client.request.duration", services: ["api", "web"]),                            // 3 + 0 + 2 = 5
                Metric("kestrel.connections", services: ["api"], keys: ["http.route", "http.request.method"]), // 0 + 4 + 1 = 5
            ],
            10);

        Assert.Equal(
            ["http.server.active_requests", "http.client.request.duration", "kestrel.connections", "dotnet.gc.collections"],
            related.Select(r => r.MetricName));

        var top = related[0];
        Assert.Equal("http.server", top.SharedNamePrefix);
        Assert.Equal(1, top.SharedAttributeKeyCount);
        Assert.Equal(1, top.SharedServiceCount);
        Assert.Null(related[2].SharedNamePrefix);
    }

    [Fact]
    public void Rank_HonorsTheLimit()
    {
        var target = Metric("app.requests", keys: ["route"]);
        var candidates = Enumerable.Range(0, 20).Select(i => Metric($"other{i}", keys: ["route"]));

        Assert.Equal(5, MetricRelatedRanker.Rank(target, candidates, 5).Count);
    }
}
