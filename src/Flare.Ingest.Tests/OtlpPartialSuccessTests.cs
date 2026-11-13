using Flare.Ingest.Otlp;
using Google.Protobuf;
using Xunit;

namespace Flare.Ingest.Tests;

public class OtlpPartialSuccessTests
{
    [Fact]
    public void ForMetrics_LeavesPartialSuccessUnset_WhenNothingWasRejected()
    {
        var response = OtlpPartialSuccess.ForMetrics(new MetricMapResult([], []));

        Assert.Null(response.PartialSuccess);
        Assert.Equal("{ }", JsonFormatter.Default.Format(response));
    }

    [Fact]
    public void ForMetrics_LeavesPartialSuccessUnset_ForAMetricWithNoDataPoints()
    {
        // A metric with no data set is listed as unsupported but carried nothing to lose.
        var response = OtlpPartialSuccess.ForMetrics(new MetricMapResult([], ["unset.metric"], 0));

        Assert.Null(response.PartialSuccess);
    }

    [Fact]
    public void ForMetrics_ReportsRejectedDataPointsAndNames()
    {
        var response = OtlpPartialSuccess.ForMetrics(new MetricMapResult([], ["a.summary", "b.summary"], 5));

        Assert.Equal(5, response.PartialSuccess.RejectedDataPoints);
        Assert.Equal(
            "Summary metrics are not supported; dropped 5 data point(s) from: a.summary, b.summary",
            response.PartialSuccess.ErrorMessage);
    }

    [Fact]
    public void ForMetrics_CapsTheListedNames()
    {
        var names = Enumerable.Range(1, OtlpPartialSuccess.MaxNamesInMessage + 3).Select(i => $"m{i}").ToList();

        var response = OtlpPartialSuccess.ForMetrics(new MetricMapResult([], names, names.Count));

        Assert.EndsWith("m10 and 3 more", response.PartialSuccess.ErrorMessage);
        Assert.DoesNotContain("m11", response.PartialSuccess.ErrorMessage);
    }
}
