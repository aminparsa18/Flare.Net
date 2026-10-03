using Flare.Ingest.Model;
using Flare.Ingest.Pipeline.MetricRules;
using Xunit;

namespace Flare.Ingest.Tests.Pipeline.MetricRules;

public class MetricAttributeReducerTests
{
    private static readonly DateTimeOffset T = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    private static MetricAttributeRule Rule(string metric, MetricAttributeRuleMode mode, params string[] attrs) =>
        new() { Id = Guid.NewGuid(), Name = "r", MetricName = metric, Mode = mode, Attributes = attrs };

    private static Dictionary<string, string> Attrs(params (string, string)[] kv) => kv.ToDictionary(x => x.Item1, x => x.Item2);

    private static SumPointRecord Sum(string name, Dictionary<string, string> attrs, double value, int temporality = 1, DateTimeOffset? time = null) => new()
    {
        MetricName = name, ServiceName = "svc", ResourceAttributes = new Dictionary<string, string>(), ScopeAttributes = new Dictionary<string, string>(),
        DataPointAttributes = attrs, Time = time ?? T, IngestedAt = T, Value = value, AggregationTemporality = temporality, IsMonotonic = true,
    };

    [Fact]
    public void Drop_removes_listed_attributes_and_sums_delta_points_that_collapse()
    {
        var points = new MetricPointRecord[]
        {
            Sum("http.requests", Attrs(("route", "/a"), ("user.id", "1")), 2),
            Sum("http.requests", Attrs(("route", "/a"), ("user.id", "2")), 3),
            Sum("http.requests", Attrs(("route", "/b"), ("user.id", "1")), 7),
        };

        var result = MetricAttributeReducer.Reduce(points, [Rule("http.requests", MetricAttributeRuleMode.Drop, "user.id")]);

        Assert.Equal(2, result.Count);
        var a = Assert.IsType<SumPointRecord>(result[0]);
        Assert.Equal(5, a.Value);
        Assert.Equal(["route"], a.DataPointAttributes.Keys);
        Assert.Equal(7, ((SumPointRecord)result[1]).Value);
    }

    [Fact]
    public void KeepOnly_removes_everything_else()
    {
        var points = new MetricPointRecord[] { Sum("m", Attrs(("a", "1"), ("b", "2"), ("c", "3")), 1) };
        var result = MetricAttributeReducer.Reduce(points, [Rule("m", MetricAttributeRuleMode.KeepOnly, "b")]);
        Assert.Equal(["b"], result[0].DataPointAttributes.Keys);
    }

    [Fact]
    public void Cumulative_sum_keeps_last_value_instead_of_adding()
    {
        var points = new MetricPointRecord[]
        {
            Sum("m", Attrs(("id", "1")), 10, temporality: 2),
            Sum("m", Attrs(("id", "2")), 12, temporality: 2),
        };
        var result = MetricAttributeReducer.Reduce(points, [Rule("m", MetricAttributeRuleMode.Drop, "id")]);
        Assert.Equal(12, Assert.IsType<SumPointRecord>(Assert.Single(result)).Value);
    }

    [Fact]
    public void Different_timestamps_are_not_merged()
    {
        var points = new MetricPointRecord[]
        {
            Sum("m", Attrs(("id", "1")), 1),
            Sum("m", Attrs(("id", "1")), 1, time: T.AddSeconds(10)),
        };
        Assert.Equal(2, MetricAttributeReducer.Reduce(points, [Rule("m", MetricAttributeRuleMode.Drop, "id")]).Count);
    }

    [Fact]
    public void Prefix_rule_matches_and_other_metrics_are_untouched()
    {
        var other = Sum("db.calls", Attrs(("id", "1")), 1);
        var points = new MetricPointRecord[] { Sum("http.client.duration", Attrs(("id", "1")), 1), other };
        var result = MetricAttributeReducer.Reduce(points, [Rule("http.client.*", MetricAttributeRuleMode.Drop, "id")]);
        Assert.Empty(result[0].DataPointAttributes);
        Assert.Same(other, result[1]);
    }

    [Fact]
    public void Delta_histograms_add_buckets_and_gauges_keep_last()
    {
        HistogramPointRecord H(string id, ulong c) => new()
        {
            MetricName = "h", ResourceAttributes = new Dictionary<string, string>(), ScopeAttributes = new Dictionary<string, string>(),
            DataPointAttributes = Attrs(("id", id)), Time = T, IngestedAt = T, AggregationTemporality = 1, Count = c, Sum = c,
            BucketCounts = [c, 0], ExplicitBounds = [1.0],
        };
        GaugePointRecord G(string id, double v) => new()
        {
            MetricName = "g", ResourceAttributes = new Dictionary<string, string>(), ScopeAttributes = new Dictionary<string, string>(),
            DataPointAttributes = Attrs(("id", id)), Time = T, IngestedAt = T, Value = v,
        };

        var result = MetricAttributeReducer.Reduce(
            [H("1", 2), H("2", 3), G("1", 5), G("2", 9)],
            [Rule("h", MetricAttributeRuleMode.Drop, "id"), Rule("g", MetricAttributeRuleMode.Drop, "id")]);

        var h = Assert.IsType<HistogramPointRecord>(result[0]);
        Assert.Equal(5ul, h.Count);
        Assert.Equal(5.0, h.Sum);
        Assert.Equal([5ul, 0ul], h.BucketCounts);
        Assert.Equal(9, Assert.IsType<GaugePointRecord>(result[1]).Value);
    }

    [Fact]
    public void Rules_that_change_nothing_return_the_same_batch()
    {
        IReadOnlyList<MetricPointRecord> points = [Sum("m", Attrs(("a", "1")), 1)];
        Assert.Same(points, MetricAttributeReducer.Reduce(points, [Rule("m", MetricAttributeRuleMode.Drop, "missing")]));
    }
}
