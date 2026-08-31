using Flare.Api.Model;
using Flare.Api.Pipeline;
using Flare.Api.Query;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Xunit;

namespace Flare.Api.Tests.Query;

public class PipelineQueryServiceTests
{
    [Fact]
    public void BuildFlushHealth_EmptyHash_AllFieldsDefaultToNullOrZero()
    {
        var health = PipelineQueryService.BuildFlushHealth(IngestionSignal.Logs, []);

        Assert.Equal(IngestionSignal.Logs, health.Signal);
        Assert.Null(health.LastFlushAt);
        Assert.Null(health.LastBatchSize);
        Assert.Null(health.LastErrorAt);
        Assert.Null(health.LastError);
        Assert.Equal(0, health.ConsecutiveErrors);
    }

    [Fact]
    public void BuildFlushHealth_ReadsSuccessFieldsBack()
    {
        var hash = new[]
        {
            new HashEntry(FlushHealthKeys.LastFlushAtField, 1_723_000_000_000),
            new HashEntry(FlushHealthKeys.LastBatchSizeField, 500),
            new HashEntry(FlushHealthKeys.ConsecutiveErrorsField, 0),
        };

        var health = PipelineQueryService.BuildFlushHealth(IngestionSignal.Metrics, hash);

        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_723_000_000_000), health.LastFlushAt);
        Assert.Equal(500, health.LastBatchSize);
        Assert.Equal(0, health.ConsecutiveErrors);
    }

    [Fact]
    public void BuildFlushHealth_ReadsErrorFieldsBack_IndependentlyOfSuccessFields()
    {
        var hash = new[]
        {
            new HashEntry(FlushHealthKeys.LastErrorAtField, 1_723_000_100_000),
            new HashEntry(FlushHealthKeys.LastErrorField, "ClickHouseServerException: timeout"),
            new HashEntry(FlushHealthKeys.ConsecutiveErrorsField, 3),
        };

        var health = PipelineQueryService.BuildFlushHealth(IngestionSignal.Traces, hash);

        Assert.Null(health.LastFlushAt);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_723_000_100_000), health.LastErrorAt);
        Assert.Equal("ClickHouseServerException: timeout", health.LastError);
        Assert.Equal(3, health.ConsecutiveErrors);
    }

    [Fact]
    public void BuildServiceBreakdown_FewerServicesThanCap_AllReturnedAsTop_NoOther()
    {
        var records = new Dictionary<string, long> { ["checkout-api"] = 10, ["orders-api"] = 5 };
        var bytes = new Dictionary<string, long> { ["checkout-api"] = 1000, ["orders-api"] = 500 };
        var skew = new Dictionary<string, long>();

        var breakdown = PipelineQueryService.BuildServiceBreakdown(IngestionSignal.Logs, records, bytes, skew);

        Assert.Equal(2, breakdown.TopServices.Count);
        Assert.Equal("checkout-api", breakdown.TopServices[0].ServiceName); // higher record count first
        Assert.Equal(0, breakdown.OtherServiceCount);
        Assert.Equal(0, breakdown.OtherRecords);
        Assert.Equal(0, breakdown.OtherBytes);
    }

    [Fact]
    public void BuildServiceBreakdown_MoreServicesThanCap_ExcessFoldedIntoOther()
    {
        var records = new Dictionary<string, long>();
        var bytes = new Dictionary<string, long>();
        for (var i = 0; i < PipelineQueryService.TopServicesPerSignal + 3; i++)
        {
            var name = $"service-{i}";
            records[name] = PipelineQueryService.TopServicesPerSignal + 3 - i; // descending, so ordering is deterministic
            bytes[name] = 100;
        }

        var breakdown = PipelineQueryService.BuildServiceBreakdown(IngestionSignal.Logs, records, bytes, skewNanosByService: new Dictionary<string, long>());

        Assert.Equal(PipelineQueryService.TopServicesPerSignal, breakdown.TopServices.Count);
        Assert.Equal(3, breakdown.OtherServiceCount);
        Assert.Equal(300, breakdown.OtherBytes);
        // The 3 lowest-record-count services are the ones excluded, not an arbitrary 3.
        Assert.DoesNotContain(breakdown.TopServices, e => e.ServiceName is "service-5" or "service-6" or "service-7");
    }

    [Fact]
    public void BuildServiceBreakdown_ServiceWithNoByteEntry_DefaultsToZeroBytes_NotAnException()
    {
        var records = new Dictionary<string, long> { ["orphan-service"] = 1 };
        var bytes = new Dictionary<string, long>();

        var breakdown = PipelineQueryService.BuildServiceBreakdown(IngestionSignal.Logs, records, bytes, skewNanosByService: new Dictionary<string, long>());

        Assert.Equal(0, Assert.Single(breakdown.TopServices).Bytes);
    }

    [Fact]
    public void BuildServiceBreakdown_ComputesAverageClockSkewMs_FromSkewNanosSumDividedByRecordCount()
    {
        var records = new Dictionary<string, long> { ["checkout-api"] = 4 };
        var bytes = new Dictionary<string, long> { ["checkout-api"] = 100 };
        // 4 records, summed skew of 20,000,000ns (20ms) -> average 5ms/record.
        var skew = new Dictionary<string, long> { ["checkout-api"] = 20_000_000 };

        var breakdown = PipelineQueryService.BuildServiceBreakdown(IngestionSignal.Logs, records, bytes, skew);

        Assert.Equal(5d, Assert.Single(breakdown.TopServices).AverageClockSkewMs);
    }

    [Fact]
    public void BuildServiceBreakdown_ServiceWithNoSkewEntry_DefaultsToZero_NotAnException()
    {
        var records = new Dictionary<string, long> { ["orphan-service"] = 1 };
        var bytes = new Dictionary<string, long>();

        var breakdown = PipelineQueryService.BuildServiceBreakdown(IngestionSignal.Logs, records, bytes, skewNanosByService: new Dictionary<string, long>());

        Assert.Equal(0d, Assert.Single(breakdown.TopServices).AverageClockSkewMs);
    }

    [Theory]
    [InlineData(IngestionSignal.Logs, "flare:logs", "flare-ingest")]
    [InlineData(IngestionSignal.Traces, "flare:spans", "flare-ingest-spans")]
    [InlineData(IngestionSignal.Metrics, "flare:metrics", "flare-ingest-metrics")]
    public void PipelineStreamKeys_DefaultOptions_MatchFlareIngestPipelineOptionsDefaults(IngestionSignal signal, string streamKey, string consumerGroup)
    {
        var keys = new PipelineStreamKeys(
            Options.Create(new LogEventPipelineOptions()),
            Options.Create(new SpanEventPipelineOptions()),
            Options.Create(new MetricEventPipelineOptions()));

        Assert.Equal(streamKey, keys.StreamKey(signal));
        Assert.Equal(consumerGroup, keys.ConsumerGroup(signal));
        Assert.Equal(1_000_000, keys.Capacity(signal)); // matches Flare.Ingest's *EventPipelineOptions.StreamMaxLength default
    }

    [Fact]
    public void PipelineStreamKeys_ConfiguredOverride_PropagatesInsteadOfHardcodedDefault()
    {
        var keys = new PipelineStreamKeys(
            Options.Create(new LogEventPipelineOptions { StreamKey = "custom:logs", ConsumerGroup = "custom-ingest", StreamMaxLength = 2_000_000 }),
            Options.Create(new SpanEventPipelineOptions()),
            Options.Create(new MetricEventPipelineOptions()));

        // The overridden signal picks up the override...
        Assert.Equal("custom:logs", keys.StreamKey(IngestionSignal.Logs));
        Assert.Equal("custom-ingest", keys.ConsumerGroup(IngestionSignal.Logs));
        Assert.Equal(2_000_000, keys.Capacity(IngestionSignal.Logs));

        // ...and an unrelated signal's own options are untouched by it (proves each signal
        // reads its own section, not a shared/leaked value).
        Assert.Equal("flare:spans", keys.StreamKey(IngestionSignal.Traces));
        Assert.Equal("flare-ingest-spans", keys.ConsumerGroup(IngestionSignal.Traces));
        Assert.Equal(1_000_000, keys.Capacity(IngestionSignal.Traces));
    }

    [Theory]
    [InlineData(IngestionSignal.Logs, "flare:ingestion:flush:logs")]
    [InlineData(IngestionSignal.Traces, "flare:ingestion:flush:traces")]
    [InlineData(IngestionSignal.Metrics, "flare:ingestion:flush:metrics")]
    public void FlushHealthKeys_HashKey_IsLowercaseSignal(IngestionSignal signal, string expected)
    {
        Assert.Equal(expected, FlushHealthKeys.HashKey(signal));
    }
}
