using Flare.Api.Model;
using Flare.Api.Query;
using Flare.Identity.MetricMetadata;
using Xunit;

namespace Flare.Api.Tests.Query;

public class MetricMetadataOverlayTests
{
    [Fact]
    public void Apply_WithNoOverride_KeepsEmittedValues()
    {
        Assert.Equal(("s", "Latency"), MetricMetadataOverlay.Apply(null, "s", "Latency"));
    }

    [Fact]
    public void Apply_OverridesEachMemberIndependently()
    {
        var unitOnly = new MetricMetadataOverride("m", "ms", null);

        Assert.Equal(("ms", "Latency"), MetricMetadataOverlay.Apply(unitOnly, "s", "Latency"));
    }

    [Fact]
    public void Apply_ToNamesResponse_RewritesOnlyOverriddenMetrics_OnEveryServiceRow()
    {
        var response = new MetricNamesResponse
        {
            Metrics =
            [
                Name("queue.depth", "api"),
                Name("queue.depth", "worker"),
                Name("http.server.duration", "api"),
            ],
        };
        var overrides = new Dictionary<string, MetricMetadataOverride>
        {
            ["queue.depth"] = new("queue.depth", "{message}", "Messages waiting"),
        };

        var result = MetricMetadataOverlay.Apply(response, overrides);

        Assert.All(result.Metrics.Where(m => m.MetricName == "queue.depth"), m =>
        {
            Assert.Equal("{message}", m.Unit);
            Assert.Equal("Messages waiting", m.Description);
        });
        Assert.Same(response.Metrics[2], result.Metrics[2]);
    }

    [Fact]
    public void Apply_ToNamesResponse_WithNoOverrides_ReturnsTheSameInstance()
    {
        var response = new MetricNamesResponse { Metrics = [Name("m", "api")] };

        Assert.Same(response, MetricMetadataOverlay.Apply(response, new Dictionary<string, MetricMetadataOverride>()));
    }

    [Fact]
    public void Validate_TrimsAndTreatsBlankAsNotOverridden()
    {
        var error = MetricMetadataOverlay.Validate(
            new SetMetricMetadataOverrideRequest { MetricName = " queue.depth ", Unit = " {message} ", Description = "  " },
            out var normalized);

        Assert.Null(error);
        Assert.Equal(new MetricMetadataOverride("queue.depth", "{message}", null), normalized);
    }

    [Fact]
    public void Validate_RejectsBothBlank()
    {
        var error = MetricMetadataOverlay.Validate(new SetMetricMetadataOverrideRequest { MetricName = "m", Unit = "", Description = null }, out _);

        Assert.NotNull(error);
    }

    [Fact]
    public void Validate_RejectsMissingName()
    {
        Assert.NotNull(MetricMetadataOverlay.Validate(new SetMetricMetadataOverrideRequest { MetricName = " ", Unit = "ms" }, out _));
    }

    [Fact]
    public void Validate_RejectsOverlongValues()
    {
        Assert.NotNull(MetricMetadataOverlay.Validate(
            new SetMetricMetadataOverrideRequest { MetricName = "m", Unit = new string('u', MetricMetadataOverlay.MaxUnitLength + 1) }, out _));
        Assert.NotNull(MetricMetadataOverlay.Validate(
            new SetMetricMetadataOverrideRequest { MetricName = "m", Description = new string('d', MetricMetadataOverlay.MaxDescriptionLength + 1) }, out _));
    }

    [Fact]
    public void Validate_AcceptsTreatAsCounterAlone()
    {
        var error = MetricMetadataOverlay.Validate(
            new SetMetricMetadataOverrideRequest { MetricName = "http_requests_total", TreatAsCounter = true }, out var normalized);

        Assert.Null(error);
        Assert.Equal(new MetricMetadataOverride("http_requests_total", null, null, TreatAsCounter: true), normalized);
    }

    [Fact]
    public void ResolveTreatAsCounter_NullTakesTheAdminSetting()
    {
        var overrides = new Dictionary<string, MetricMetadataOverride>
        {
            ["http_requests_total"] = new("http_requests_total", null, null, TreatAsCounter: true),
            ["queue.depth"] = new("queue.depth", "{message}", null),
        };

        Assert.True(MetricMetadataOverlay.ResolveTreatAsCounter(Query("http_requests_total", null), overrides).TreatAsCounter);
        Assert.False(MetricMetadataOverlay.ResolveTreatAsCounter(Query("queue.depth", null), overrides).TreatAsCounter);
        Assert.False(MetricMetadataOverlay.ResolveTreatAsCounter(Query("no.override", null), overrides).TreatAsCounter);
    }

    [Fact]
    public void ResolveTreatAsCounter_ExplicitValueWins()
    {
        var overrides = new Dictionary<string, MetricMetadataOverride>
        {
            ["http_requests_total"] = new("http_requests_total", null, null, TreatAsCounter: true),
        };

        Assert.False(MetricMetadataOverlay.ResolveTreatAsCounter(Query("http_requests_total", false), overrides).TreatAsCounter);
    }

    private static MetricQueryRequest Query(string metric, bool? treatAsCounter) =>
        new() { MetricName = metric, Type = MetricPointType.Gauge, BucketWidthSeconds = 60, TreatAsCounter = treatAsCounter };

    private static MetricNameInfo Name(string metric, string service) =>
        new() { MetricName = metric, ServiceName = service, Type = MetricPointType.Gauge, Unit = "1", Description = "emitted", SeriesCount = 1 };
}
