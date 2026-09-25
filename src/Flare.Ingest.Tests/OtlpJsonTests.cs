using System.Text;
using Flare.Ingest.Otlp;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using Xunit;

namespace Flare.Ingest.Tests;

public class OtlpJsonTests
{
    private const string TraceIdHex = "0af7651916cd43dd8448eb211c80319c";
    private const string SpanIdHex = "b7ad6b7169203331";
    private const string ParentIdHex = "00f067aa0ba902b7";

    private static T Parse<T>(string json)
        where T : Google.Protobuf.IMessage<T>, new()
        => OtlpJson.Parse<T>(Encoding.UTF8.GetBytes(json));

    [Fact]
    public void Parse_DecodesHexSpanIdsLinksIncluded()
    {
        var request = Parse<ExportTraceServiceRequest>($$$"""
            {"resourceSpans":[{"scopeSpans":[{"spans":[{
              "traceId":"{{{TraceIdHex}}}","spanId":"{{{SpanIdHex}}}","parentSpanId":"{{{ParentIdHex}}}","name":"op",
              "links":[{"traceId":"{{{TraceIdHex}}}","spanId":"{{{ParentIdHex}}}"}]
            }]}]}]}
            """);

        var span = request.ResourceSpans[0].ScopeSpans[0].Spans[0];
        Assert.Equal(TraceIdHex, Convert.ToHexStringLower(span.TraceId.ToByteArray()));
        Assert.Equal(SpanIdHex, Convert.ToHexStringLower(span.SpanId.ToByteArray()));
        Assert.Equal(ParentIdHex, Convert.ToHexStringLower(span.ParentSpanId.ToByteArray()));
        Assert.Equal(ParentIdHex, Convert.ToHexStringLower(span.Links[0].SpanId.ToByteArray()));
        Assert.Equal("op", span.Name);
    }

    [Fact]
    public void Parse_DecodesHexLogRecordIds_AndKeepsOtherFields()
    {
        var request = Parse<ExportLogsServiceRequest>($$$"""
            {"resourceLogs":[{"scopeLogs":[{"logRecords":[{
              "timeUnixNano":"1700000000000000000","severityNumber":9,
              "body":{"stringValue":"héllo \"quoted\" 日本"},
              "trace_id":"{{{TraceIdHex}}}","span_id":"{{{SpanIdHex}}}"
            }]}]}]}
            """);

        var record = request.ResourceLogs[0].ScopeLogs[0].LogRecords[0];
        Assert.Equal(TraceIdHex, Convert.ToHexStringLower(record.TraceId.ToByteArray()));
        Assert.Equal(SpanIdHex, Convert.ToHexStringLower(record.SpanId.ToByteArray()));
        Assert.Equal(1700000000000000000UL, record.TimeUnixNano);
        Assert.Equal("héllo \"quoted\" 日本", record.Body.StringValue);
    }

    [Fact]
    public void Parse_DecodesHexExemplarIds()
    {
        var request = Parse<ExportMetricsServiceRequest>($$$"""
            {"resourceMetrics":[{"scopeMetrics":[{"metrics":[{"name":"m","sum":{"dataPoints":[{
              "asInt":"9007199254740993",
              "exemplars":[{"asDouble":1.5,"traceId":"{{{TraceIdHex}}}","spanId":"{{{SpanIdHex}}}"}]
            }]}}]}]}]}
            """);

        var point = request.ResourceMetrics[0].ScopeMetrics[0].Metrics[0].Sum.DataPoints[0];
        Assert.Equal(9007199254740993L, point.AsInt);
        Assert.Equal(TraceIdHex, Convert.ToHexStringLower(point.Exemplars[0].TraceId.ToByteArray()));
    }

    [Fact]
    public void NormalizeIds_PassesNumbersThroughDigitForDigit()
    {
        // Bare JSON numbers past 2^53 lose precision inside JsonParser itself (which is
        // why OTLP/JSON sends int64s as strings) - the rewrite mustn't add a second loss.
        var json = """{"asInt":9007199254740993,"asDouble":1.5e-7}""";

        Assert.Equal(json, OtlpJson.NormalizeIds(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void Parse_LeavesBase64IdsAlone()
    {
        var base64 = Convert.ToBase64String(Convert.FromHexString(TraceIdHex));

        var request = Parse<ExportTraceServiceRequest>($$$"""
            {"resourceSpans":[{"scopeSpans":[{"spans":[{"traceId":"{{{base64}}}","name":"op"}]}]}]}
            """);

        Assert.Equal(TraceIdHex, Convert.ToHexStringLower(request.ResourceSpans[0].ScopeSpans[0].Spans[0].TraceId.ToByteArray()));
    }

    [Fact]
    public void NormalizeIds_LeavesEmptyAndNonIdStringsUntouched()
    {
        var json = $$$"""{"spanId":"","name":"{{{TraceIdHex}}}","attributes":[{"key":"traceId","value":{"stringValue":"{{{TraceIdHex}}}"}}]}""";

        Assert.Equal(json, OtlpJson.NormalizeIds(Encoding.UTF8.GetBytes(json)));
    }
}
