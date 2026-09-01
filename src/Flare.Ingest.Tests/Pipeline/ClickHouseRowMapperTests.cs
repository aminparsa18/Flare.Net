using Flare.Ingest.Model;
using Flare.Ingest.Pipeline;
using Xunit;

namespace Flare.Ingest.Tests.Pipeline;

public class ClickHouseRowMapperTests
{
    [Fact]
    public void Columns_MatchLogsTableColumnOrder()
    {
        // Mirrors db/clickhouse/0001_logs.sql's column declaration order, with EventId
        // (0002_logs_event_id.sql) then PatternId/PatternTemplate (0010_logs_pattern.sql)
        // appended via their own ALTER TABLE ... ADD COLUMN migrations - all pair with
        // row values positionally via InsertBinaryAsync.
        Assert.Equal(
            [
                "Timestamp", "ObservedTimestamp", "TraceId", "SpanId", "TraceFlags",
                "SeverityText", "SeverityNumber", "ServiceName", "Body", "ResourceSchemaUrl",
                "ResourceAttributes", "ScopeSchemaUrl", "ScopeName", "ScopeVersion",
                "ScopeAttributes", "LogAttributes", "EventName", "EventId",
                "PatternId", "PatternTemplate", "IngestedAt",
            ],
            ClickHouseRowMapper.Columns);
    }

    [Fact]
    public void ToRow_PassesThroughIngestedAt_AsTheLastColumn()
    {
        var ingestedAt = new DateTimeOffset(2026, 8, 7, 12, 0, 5, TimeSpan.Zero);
        var logEvent = MinimalLogEvent() with { IngestedAt = ingestedAt };

        var row = ClickHouseRowMapper.ToRow(logEvent);

        Assert.Equal(ingestedAt.UtcDateTime, row[20]);
    }

    [Fact]
    public void ToRow_HasOneValuePerColumn()
    {
        var row = ClickHouseRowMapper.ToRow(MinimalLogEvent());

        Assert.Equal(ClickHouseRowMapper.Columns.Count, row.Length);
    }

    [Fact]
    public void ToRow_CoalescesEveryNullableString_ToEmptyString()
    {
        var row = ClickHouseRowMapper.ToRow(MinimalLogEvent());

        // Index positions match Columns: TraceId=2, SpanId=3, SeverityText=5,
        // ServiceName=7, Body=8, ResourceSchemaUrl=9, ScopeSchemaUrl=11, ScopeName=12,
        // ScopeVersion=13, EventName=16.
        foreach (var index in new[] { 2, 3, 5, 7, 8, 9, 11, 12, 13, 16 })
        {
            Assert.Equal(string.Empty, row[index]);
        }
    }

    [Fact]
    public void ToRow_FallsBackObservedTimestampToTimestamp_WhenObservedTimestampIsNull()
    {
        var timestamp = new DateTimeOffset(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);
        var logEvent = MinimalLogEvent() with { Timestamp = timestamp, ObservedTimestamp = null };

        var row = ClickHouseRowMapper.ToRow(logEvent);

        Assert.Equal(timestamp.UtcDateTime, row[0]);
        Assert.Equal(timestamp.UtcDateTime, row[1]);
    }

    [Fact]
    public void ToRow_UsesObservedTimestamp_WhenSet()
    {
        var timestamp = new DateTimeOffset(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);
        var observed = timestamp.AddSeconds(5);
        var logEvent = MinimalLogEvent() with { Timestamp = timestamp, ObservedTimestamp = observed };

        var row = ClickHouseRowMapper.ToRow(logEvent);

        Assert.Equal(timestamp.UtcDateTime, row[0]);
        Assert.Equal(observed.UtcDateTime, row[1]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(24)]
    public void ToRow_CastsSeverityNumber_ToByte_AtBoundaryValues(int severityNumber)
    {
        var logEvent = MinimalLogEvent() with { SeverityNumber = severityNumber };

        var row = ClickHouseRowMapper.ToRow(logEvent);

        Assert.Equal((byte)severityNumber, row[6]);
    }

    [Fact]
    public void ToRow_PassesThroughAttributeMaps_AsDictionaries()
    {
        var logEvent = MinimalLogEvent() with
        {
            ResourceAttributes = new Dictionary<string, string> { ["service.name"] = "flare-ingest" },
            ScopeAttributes = new Dictionary<string, string> { ["scope.key"] = "scope-value" },
            LogAttributes = new Dictionary<string, string> { ["http.method"] = "GET" },
        };

        var row = ClickHouseRowMapper.ToRow(logEvent);

        Assert.Equal(new Dictionary<string, string> { ["service.name"] = "flare-ingest" }, (Dictionary<string, string>)row[10]);
        Assert.Equal(new Dictionary<string, string> { ["scope.key"] = "scope-value" }, (Dictionary<string, string>)row[14]);
        Assert.Equal(new Dictionary<string, string> { ["http.method"] = "GET" }, (Dictionary<string, string>)row[15]);
    }

    [Fact]
    public void ToRows_MapsEachEventInOrder()
    {
        var first = MinimalLogEvent() with { Body = "first" };
        var second = MinimalLogEvent() with { Body = "second" };

        var rows = ClickHouseRowMapper.ToRows([first, second]);

        Assert.Equal(2, rows.Count);
        Assert.Equal("first", rows[0][8]);
        Assert.Equal("second", rows[1][8]);
    }

    [Fact]
    public void ToRow_PassesThroughEventId_AtItsColumnIndex()
    {
        var eventId = Guid.NewGuid();
        var logEvent = MinimalLogEvent() with { EventId = eventId };

        var row = ClickHouseRowMapper.ToRow(logEvent);

        Assert.Equal(eventId, row[17]);
    }

    [Fact]
    public void ToRow_PassesThroughPatternIdAndTemplate_AsTheLastTwoColumns()
    {
        var logEvent = MinimalLogEvent() with { PatternId = "abc123", PatternTemplate = "GET /api/orders/<*>" };

        var row = ClickHouseRowMapper.ToRow(logEvent);

        Assert.Equal("abc123", row[18]);
        Assert.Equal("GET /api/orders/<*>", row[19]);
    }

    [Fact]
    public void ToRow_DefaultsPatternIdAndTemplate_ToEmptyString()
    {
        var row = ClickHouseRowMapper.ToRow(MinimalLogEvent());

        Assert.Equal(string.Empty, row[18]);
        Assert.Equal(string.Empty, row[19]);
    }

    private static LogEvent MinimalLogEvent() => new()
    {
        EventId = Guid.NewGuid(),
        Timestamp = DateTimeOffset.UnixEpoch,
        IngestedAt = DateTimeOffset.UnixEpoch,
        SeverityNumber = 9,
        ResourceAttributes = new Dictionary<string, string>(),
        ScopeAttributes = new Dictionary<string, string>(),
        LogAttributes = new Dictionary<string, string>(),
    };
}
