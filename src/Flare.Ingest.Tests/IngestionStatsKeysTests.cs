using System.Text.Json;
using Flare.Ingest.Stats;
using Xunit;

namespace Flare.Ingest.Tests;

public class IngestionStatsKeysTests
{
    [Fact]
    public void MinuteBucketKey_TwoTimestampsInSameMinute_ProduceTheSameKey()
    {
        var first = new DateTimeOffset(2026, 8, 10, 12, 30, 0, TimeSpan.Zero);
        var second = new DateTimeOffset(2026, 8, 10, 12, 30, 59, TimeSpan.Zero);

        Assert.Equal(IngestionStatsKeys.MinuteBucketKey(first), IngestionStatsKeys.MinuteBucketKey(second));
    }

    [Fact]
    public void MinuteBucketKey_TimestampsInDifferentMinutes_ProduceDifferentKeys()
    {
        var first = new DateTimeOffset(2026, 8, 10, 12, 30, 59, TimeSpan.Zero);
        var second = new DateTimeOffset(2026, 8, 10, 12, 31, 0, TimeSpan.Zero);

        Assert.NotEqual(IngestionStatsKeys.MinuteBucketKey(first), IngestionStatsKeys.MinuteBucketKey(second));
    }

    [Fact]
    public void MinuteBucketKey_RoundTripsThroughParseMinuteBucketKey()
    {
        var timestamp = new DateTimeOffset(2026, 8, 10, 12, 30, 0, TimeSpan.Zero);
        var expectedMinute = timestamp.ToUnixTimeSeconds() / 60;

        var key = IngestionStatsKeys.MinuteBucketKey(timestamp);

        Assert.Equal(expectedMinute, IngestionStatsKeys.ParseMinuteBucketKey(key));
    }

    [Theory]
    [InlineData(IngestionSignal.Logs, IngestionProtocol.Http, "logs:http")]
    [InlineData(IngestionSignal.Logs, IngestionProtocol.Grpc, "logs:grpc")]
    [InlineData(IngestionSignal.Traces, IngestionProtocol.Http, "traces:http")]
    [InlineData(IngestionSignal.Traces, IngestionProtocol.Grpc, "traces:grpc")]
    [InlineData(IngestionSignal.Metrics, IngestionProtocol.Http, "metrics:http")]
    [InlineData(IngestionSignal.Metrics, IngestionProtocol.Grpc, "metrics:grpc")]
    public void FieldPrefix_IsLowercaseSignalColonProtocol(IngestionSignal signal, IngestionProtocol protocol, string expected)
    {
        Assert.Equal(expected, IngestionStatsKeys.FieldPrefix(signal, protocol));
    }

    [Fact]
    public void ServiceRecordsKey_AndServiceBytesKey_DifferForSameMinuteAndSignal()
    {
        var timestamp = new DateTimeOffset(2026, 8, 10, 12, 30, 0, TimeSpan.Zero);

        Assert.NotEqual(
            IngestionStatsKeys.ServiceRecordsKey(timestamp, IngestionSignal.Logs),
            IngestionStatsKeys.ServiceBytesKey(timestamp, IngestionSignal.Logs));
    }

    [Fact]
    public void ServiceRecordsKey_TimestampOverload_MatchesEpochMinuteOverload()
    {
        var timestamp = new DateTimeOffset(2026, 8, 10, 12, 30, 0, TimeSpan.Zero);
        var epochMinute = timestamp.ToUnixTimeSeconds() / 60;

        Assert.Equal(
            IngestionStatsKeys.ServiceRecordsKey(timestamp, IngestionSignal.Traces),
            IngestionStatsKeys.ServiceRecordsKey(epochMinute, IngestionSignal.Traces));
    }

    [Fact]
    public void ServiceRecordsKey_DifferentSignals_ProduceDifferentKeys()
    {
        var timestamp = new DateTimeOffset(2026, 8, 10, 12, 30, 0, TimeSpan.Zero);

        Assert.NotEqual(
            IngestionStatsKeys.ServiceRecordsKey(timestamp, IngestionSignal.Logs),
            IngestionStatsKeys.ServiceRecordsKey(timestamp, IngestionSignal.Metrics));
    }
}

public class IngestionErrorEntryJsonContextTests
{
    [Fact]
    public void RoundTrips_ThroughSourceGeneratedContext()
    {
        var entry = new IngestionErrorEntry(
            new DateTimeOffset(2026, 8, 10, 12, 30, 0, TimeSpan.Zero),
            IngestionSignal.Logs.ToString(),
            IngestionProtocol.Http.ToString(),
            "invalid-payload:InvalidProtocolBufferException");

        var json = JsonSerializer.Serialize(entry, IngestionErrorEntryJsonContext.Default.IngestionErrorEntry);
        var roundTripped = JsonSerializer.Deserialize(json, IngestionErrorEntryJsonContext.Default.IngestionErrorEntry);

        Assert.Equal(entry, roundTripped);
    }
}
