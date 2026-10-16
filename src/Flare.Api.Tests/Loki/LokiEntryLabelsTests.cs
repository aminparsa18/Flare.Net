using System.Text;
using System.Text.Json;
using Flare.Api.Loki;
using Xunit;

namespace Flare.Api.Tests.Loki;

public class LokiEntryLabelsTests
{
    private static LokiLogRow Row(string body = "hello", byte severity = 17, Dictionary<string, string>? resource = null, Dictionary<string, string>? log = null) => new(
        Ns: 1_760_000_000_123_456_789,
        Body: body,
        ServiceName: "api",
        SeverityText: "Error",
        SeverityNumber: severity,
        TraceId: "abc123",
        SpanId: "def456",
        ScopeName: "My.Scope",
        ScopeVersion: "",
        ResourceAttributes: resource ?? new() { ["service.name"] = "api", ["k8s.pod.name"] = "web-1", ["host.name"] = "h1" },
        LogAttributes: log ?? new() { ["http.method"] = "GET" });

    [Fact]
    public void Stream_IsServiceAndIndexedResourceAttributes()
    {
        var entry = LokiEntryLabels.Build(Row(), null);

        Assert.Equal(new SortedDictionary<string, string> { ["k8s_pod_name"] = "web-1", ["service_name"] = "api" }, entry.StreamLabels);
        Assert.Equal(1_760_000_000_123_456_789, entry.Ns);
        Assert.Equal("hello", entry.Line);
    }

    [Fact]
    public void StructuredMetadata_HoldsLevelIdsScopeAndTheRestOfTheAttributes()
    {
        var metadata = LokiEntryLabels.Build(Row(), null).StructuredMetadata;

        Assert.Equal("error", metadata["detected_level"]);
        Assert.Equal("Error", metadata["severity_text"]);
        Assert.Equal("abc123", metadata["trace_id"]);
        Assert.Equal("def456", metadata["span_id"]);
        Assert.Equal("My.Scope", metadata["scope_name"]);
        Assert.Equal("GET", metadata["http_method"]);
        Assert.Equal("h1", metadata["host_name"]);
        Assert.DoesNotContain("scope_version", metadata.Keys);
        Assert.DoesNotContain("service_name", metadata.Keys);
    }

    [Fact]
    public void Json_FlattensNestedKeysWithUnderscores()
    {
        var parsed = LokiEntryLabels.Build(
            Row("""{"status":500,"request":{"method":"GET","path":"/x"},"ok":false,"tags":["a","b"],"gone":null}"""),
            new LokiJsonSpec(new Dictionary<string, string>())).Parsed;

        Assert.Equal("500", parsed["status"]);
        Assert.Equal("GET", parsed["request_method"]);
        Assert.Equal("/x", parsed["request_path"]);
        Assert.Equal("false", parsed["ok"]);
        Assert.Equal("""["a","b"]""", parsed["tags"]);
        Assert.DoesNotContain("gone", parsed.Keys);
    }

    [Fact]
    public void Json_ExplicitExpressionsExtractOnlyThoseLabels()
    {
        var parsed = LokiEntryLabels.Build(
            Row("""{"user":{"id":"42"},"other":1}"""),
            new LokiJsonSpec(new Dictionary<string, string> { ["uid"] = "user.id", ["missing"] = "no.such" })).Parsed;

        Assert.Equal(new Dictionary<string, string> { ["uid"] = "42" }, parsed);
    }

    [Fact]
    public void Json_CollidingLabelsGetTheExtractedSuffix()
    {
        var parsed = LokiEntryLabels.Build(Row("""{"service_name":"x","trace_id":"y"}"""), new LokiJsonSpec(new Dictionary<string, string>())).Parsed;

        Assert.Equal("x", parsed["service_name_extracted"]);
        Assert.Equal("y", parsed["trace_id_extracted"]);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("{broken")]
    public void Json_NonObjectBodySetsTheParserError(string body)
    {
        var parsed = LokiEntryLabels.Build(Row(body), new LokiJsonSpec(new Dictionary<string, string>())).Parsed;

        Assert.Equal("JSONParserErr", Assert.Single(parsed).Value);
    }

    [Fact]
    public void NoJsonStage_NoParsedLabels() =>
        Assert.Empty(LokiEntryLabels.Build(Row("""{"a":1}"""), null).Parsed);

    private static string Write(IReadOnlyList<LokiLogEntry> entries, bool categorize)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            LokiEndpoints.WriteStreams(writer, entries, categorize);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    [Fact]
    public void WriteStreams_Categorized_GroupsByStreamAndCarriesMetadata()
    {
        var entries = new[]
        {
            LokiEntryLabels.Build(Row("one"), null),
            LokiEntryLabels.Build(Row("two", severity: 9), null),
            LokiEntryLabels.Build(Row("three", resource: new() { ["service.name"] = "other" }) with { ServiceName = "other" }, null),
        };

        using var json = JsonDocument.Parse(Write(entries, categorize: true));
        var result = json.RootElement.GetProperty("result");

        Assert.Equal("streams", json.RootElement.GetProperty("resultType").GetString());
        Assert.Equal("categorize-labels", json.RootElement.GetProperty("encodingFlags")[0].GetString());
        Assert.Equal(2, result.GetArrayLength());
        var first = result[0];
        Assert.Equal("api", first.GetProperty("stream").GetProperty("service_name").GetString());
        var values = first.GetProperty("values");
        Assert.Equal(2, values.GetArrayLength());
        Assert.Equal("1760000000123456789", values[0][0].GetString());
        Assert.Equal("one", values[0][1].GetString());
        Assert.Equal("error", values[0][2].GetProperty("structuredMetadata").GetProperty("detected_level").GetString());
        Assert.Equal("info", values[1][2].GetProperty("structuredMetadata").GetProperty("detected_level").GetString());
    }

    [Fact]
    public void WriteStreams_Plain_MergesMetadataIntoTheStreamLabels()
    {
        using var json = JsonDocument.Parse(Write([LokiEntryLabels.Build(Row("one"), null)], categorize: false));
        var stream = json.RootElement.GetProperty("result")[0];

        Assert.False(json.RootElement.TryGetProperty("encodingFlags", out _));
        Assert.Equal("error", stream.GetProperty("stream").GetProperty("detected_level").GetString());
        Assert.Equal(2, stream.GetProperty("values")[0].GetArrayLength());
    }

    [Theory]
    [InlineData("1760000000123456789", 1760000000123L)]
    [InlineData("1760000000.5", 1760000000500L)]
    [InlineData("1760000000", 1760000000000L)]
    [InlineData("1760000000123", 1760000000123L)]
    [InlineData("2025-10-09T08:53:20Z", 1760000000000L)]
    public void ParseTime_AcceptsNanosecondsSecondsMillisecondsAndRfc3339(string text, long expectedMs) =>
        Assert.Equal(expectedMs, LokiEndpoints.ParseTime(text, "start").ToUnixTimeMilliseconds());

    [Fact]
    public void ParseTime_RejectsGarbage() =>
        Assert.Throws<LokiException>(() => LokiEndpoints.ParseTime("yesterday", "start"));

    [Theory]
    [InlineData("15", 15)]
    [InlineData("0.5", 0.5)]
    [InlineData("1m", 60)]
    public void ParseStep_AcceptsSecondsAndDurations(string text, double seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), LokiEndpoints.ParseStep(text));

    [Fact]
    public void FormatSample_UsesPrometheusSpelling()
    {
        Assert.Equal("+Inf", LokiEndpoints.FormatSample(double.PositiveInfinity));
        Assert.Equal("NaN", LokiEndpoints.FormatSample(double.NaN));
        Assert.Equal("0.5", LokiEndpoints.FormatSample(0.5));
    }
}
