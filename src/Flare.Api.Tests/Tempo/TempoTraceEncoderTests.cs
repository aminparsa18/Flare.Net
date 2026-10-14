using System.Text.Json;
using Flare.Api.Model;
using Flare.Api.Tempo;
using Google.Protobuf;
using OpenTelemetry.Proto.Trace.V1;
using OtlpSpan = OpenTelemetry.Proto.Trace.V1.Span;
using OtlpStatus = OpenTelemetry.Proto.Trace.V1.Status;
using Xunit;

namespace Flare.Api.Tests.Tempo;

public class TempoTraceEncoderTests
{
    private const string TraceId = "0af7651916cd43dd8448eb211c80319c";
    private static readonly DateTimeOffset Start = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    private static SpanDto MakeSpan(
        string spanId,
        string parentSpanId = "",
        string service = "api",
        string name = "GET /",
        string statusCode = "STATUS_CODE_UNSET",
        string statusMessage = "",
        byte kind = 2,
        IReadOnlyDictionary<string, string>? spanAttributes = null,
        IReadOnlyDictionary<string, string>? resourceAttributes = null,
        string scope = "app",
        IReadOnlyList<SpanEventDto>? events = null,
        IReadOnlyList<SpanLinkDto>? links = null) => new()
    {
        TraceId = TraceId,
        SpanId = spanId,
        ParentSpanId = parentSpanId,
        TraceState = "",
        Name = name,
        Kind = kind,
        StartTime = Start,
        EndTime = Start.AddMilliseconds(250),
        IngestedAt = Start,
        DurationNano = 250_000_000,
        StatusCode = statusCode,
        StatusMessage = statusMessage,
        ServiceName = service,
        ResourceSchemaUrl = "",
        ResourceAttributes = resourceAttributes ?? new Dictionary<string, string> { ["service.name"] = service },
        ScopeSchemaUrl = "",
        ScopeName = scope,
        ScopeVersion = "1.0",
        ScopeAttributes = new Dictionary<string, string>(),
        SpanAttributes = spanAttributes ?? new Dictionary<string, string>(),
        Events = events ?? [],
        Links = links ?? [],
    };

    [Fact]
    public void Protobuf_RoundTripsThroughTheOtlpTypes()
    {
        var spans = new[]
        {
            MakeSpan("00f067aa0ba902b7", name: "GET /pay", statusCode: "STATUS_CODE_ERROR", statusMessage: "boom",
                spanAttributes: new Dictionary<string, string> { ["http.method"] = "GET", ["empty"] = "" },
                events: [new SpanEventDto { Timestamp = Start.AddMilliseconds(5), Name = "exception", Attributes = new Dictionary<string, string> { ["exception.type"] = "Boom" } }],
                links: [new SpanLinkDto { TraceId = "11111111111111111111111111111111", SpanId = "2222222222222222", TraceState = "k=v", Attributes = new Dictionary<string, string> { ["l"] = "1" } }]),
            MakeSpan("b7ad6b7169203331", parentSpanId: "00f067aa0ba902b7", name: "SELECT", kind: 3),
        };

        var data = TracesData.Parser.ParseFrom(TempoTraceEncoder.ToTraceProtobuf(spans));

        var resource = Assert.Single(data.ResourceSpans);
        Assert.Equal("service.name", Assert.Single(resource.Resource.Attributes).Key);
        Assert.Equal("api", resource.Resource.Attributes[0].Value.StringValue);
        var scope = Assert.Single(resource.ScopeSpans);
        Assert.Equal(("app", "1.0"), (scope.Scope.Name, scope.Scope.Version));
        Assert.Equal(2, scope.Spans.Count);

        var root = scope.Spans[0];
        Assert.Equal(ByteString.CopyFrom(Convert.FromHexString(TraceId)), root.TraceId);
        Assert.Equal(ByteString.CopyFrom(Convert.FromHexString("00f067aa0ba902b7")), root.SpanId);
        Assert.True(root.ParentSpanId.IsEmpty);
        Assert.Equal("GET /pay", root.Name);
        Assert.Equal(OtlpSpan.Types.SpanKind.Server, root.Kind);
        Assert.Equal((ulong)(Start.ToUnixTimeMilliseconds() * 1_000_000), root.StartTimeUnixNano);
        Assert.Equal(root.StartTimeUnixNano + 250_000_000, root.EndTimeUnixNano);
        Assert.Equal(OtlpStatus.Types.StatusCode.Error, root.Status.Code);
        Assert.Equal("boom", root.Status.Message);
        Assert.Equal(["empty", "http.method"], root.Attributes.Select(a => a.Key));
        Assert.Equal("", root.Attributes[0].Value.StringValue);
        Assert.Equal(AnyValueCase(root.Attributes[0].Value), AnyValueCase(root.Attributes[1].Value));
        var ev = Assert.Single(root.Events);
        Assert.Equal(("exception", root.StartTimeUnixNano + 5_000_000), (ev.Name, ev.TimeUnixNano));
        Assert.Equal("Boom", ev.Attributes[0].Value.StringValue);
        var link = Assert.Single(root.Links);
        Assert.Equal(ByteString.CopyFrom(Convert.FromHexString("2222222222222222")), link.SpanId);
        Assert.Equal("k=v", link.TraceState);

        var child = scope.Spans[1];
        Assert.Equal(ByteString.CopyFrom(Convert.FromHexString("00f067aa0ba902b7")), child.ParentSpanId);
        Assert.Equal(OtlpSpan.Types.SpanKind.Client, child.Kind);
        // An unset status is still written: Grafana's Tempo data source reads span.Status.Code without a nil check.
        Assert.NotNull(child.Status);
        Assert.Equal(OtlpStatus.Types.StatusCode.Unset, child.Status.Code);
    }

    private static object AnyValueCase(OpenTelemetry.Proto.Common.V1.AnyValue value) => value.ValueCase;

    [Fact]
    public void SpansWithDifferentResourcesOrScopes_AreGroupedAsOtlpNestsThem()
    {
        var spans = new[]
        {
            MakeSpan("0000000000000001", service: "api", scope: "http"),
            MakeSpan("0000000000000002", service: "db", scope: "sql"),
            MakeSpan("0000000000000003", service: "api", scope: "http"),
            MakeSpan("0000000000000004", service: "api", scope: "cache"),
        };

        var data = TracesData.Parser.ParseFrom(TempoTraceEncoder.ToTraceProtobuf(spans));

        Assert.Equal(2, data.ResourceSpans.Count);
        var api = data.ResourceSpans[0];
        Assert.Equal(["http", "cache"], api.ScopeSpans.Select(s => s.Scope.Name));
        Assert.Equal(2, api.ScopeSpans[0].Spans.Count);
        Assert.Single(data.ResourceSpans[1].ScopeSpans);
    }

    [Fact]
    public void ByIdResponse_WrapsTheTraceAndCarriesThePartialStatus()
    {
        var bytes = TempoTraceEncoder.ToTraceByIdResponseProtobuf([MakeSpan("0000000000000001")], "cut short");

        var input = new CodedInputStream(bytes);
        TracesData? trace = null;
        ulong status = 0;
        string? message = null;
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (tag >> 3)
            {
                case 1: trace = TracesData.Parser.ParseFrom(input.ReadBytes()); break;
                case 3: status = input.ReadUInt64(); break;
                case 4: message = input.ReadString(); break;
                default: input.SkipLastField(); break;
            }
        }

        Assert.Single(trace!.ResourceSpans);
        Assert.Equal((1UL, "cut short"), (status, message));
    }

    [Fact]
    public void ByIdResponse_OfACompleteTrace_HasTheTraceAndMetricsButNoStatus()
    {
        var bytes = TempoTraceEncoder.ToTraceByIdResponseProtobuf([MakeSpan("0000000000000001")], null);

        var input = new CodedInputStream(bytes);
        var fields = new List<uint>();
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            fields.Add(tag >> 3);
            input.SkipLastField();
        }

        // Grafana's data source dereferences the metrics submessage, so it is always sent.
        Assert.Equal([1u, 2u], fields);
    }

    [Fact]
    public void V1Json_IsBatchesOfOtlpJson()
    {
        var spans = new[]
        {
            MakeSpan("00f067aa0ba902b7", statusCode: "STATUS_CODE_ERROR", statusMessage: "boom", spanAttributes: new Dictionary<string, string> { ["k"] = "v" }),
            MakeSpan("b7ad6b7169203331", parentSpanId: "00f067aa0ba902b7", kind: 3),
        };

        using var doc = JsonDocument.Parse(TempoTraceEncoder.ToTraceJson(spans));

        var batch = Assert.Single(doc.RootElement.GetProperty("batches").EnumerateArray());
        Assert.Equal("service.name", batch.GetProperty("resource").GetProperty("attributes")[0].GetProperty("key").GetString());
        var jsonSpans = batch.GetProperty("scopeSpans")[0].GetProperty("spans");
        var root = jsonSpans[0];
        Assert.Equal(TraceId, root.GetProperty("traceId").GetString());
        Assert.False(root.TryGetProperty("parentSpanId", out _));
        Assert.Equal("SPAN_KIND_SERVER", root.GetProperty("kind").GetString());
        Assert.Equal(((ulong)Start.ToUnixTimeMilliseconds() * 1_000_000).ToString(), root.GetProperty("startTimeUnixNano").GetString());
        Assert.Equal("v", root.GetProperty("attributes")[0].GetProperty("value").GetProperty("stringValue").GetString());
        Assert.Equal("STATUS_CODE_ERROR", root.GetProperty("status").GetProperty("code").GetString());
        Assert.Equal("boom", root.GetProperty("status").GetProperty("message").GetString());
        Assert.Equal("00f067aa0ba902b7", jsonSpans[1].GetProperty("parentSpanId").GetString());
        Assert.False(jsonSpans[1].TryGetProperty("status", out _));
    }

    [Fact]
    public void V2Json_WrapsTheTraceAndNamesAPartialStatus()
    {
        using var partial = JsonDocument.Parse(TempoTraceEncoder.ToTraceByIdResponseJson([MakeSpan("0000000000000001")], "cut short"));
        using var complete = JsonDocument.Parse(TempoTraceEncoder.ToTraceByIdResponseJson([MakeSpan("0000000000000001")], null));

        Assert.Single(partial.RootElement.GetProperty("trace").GetProperty("resourceSpans").EnumerateArray());
        Assert.Equal(("PARTIAL", "cut short"), (partial.RootElement.GetProperty("status").GetString(), partial.RootElement.GetProperty("message").GetString()));
        Assert.False(complete.RootElement.TryGetProperty("status", out _));
    }

    [Fact]
    public void AnUnreadableId_IsOmittedRatherThanFailingTheWholeTrace()
    {
        var span = MakeSpan("not-hex");

        var data = TracesData.Parser.ParseFrom(TempoTraceEncoder.ToTraceProtobuf([span]));

        Assert.True(Assert.Single(Assert.Single(data.ResourceSpans[0].ScopeSpans).Spans).SpanId.IsEmpty);
    }

    [Theory]
    [InlineData("0af7651916cd43dd8448eb211c80319c", "0af7651916cd43dd8448eb211c80319c")]
    [InlineData("0AF7651916CD43DD8448EB211C80319C", "0af7651916cd43dd8448eb211c80319c")]
    [InlineData("abc", "00000000000000000000000000000abc")]
    public void TraceIds_AreLeftPaddedAndLowerCased(string input, string expected) =>
        Assert.Equal(expected, TempoEndpoints.NormalizeTraceId(input));

    [Theory]
    [InlineData("")]
    [InlineData("xyz")]
    [InlineData("0af7651916cd43dd8448eb211c80319c0")]
    public void InvalidTraceIds_AreRejected(string input) => Assert.Null(TempoEndpoints.NormalizeTraceId(input));
}
