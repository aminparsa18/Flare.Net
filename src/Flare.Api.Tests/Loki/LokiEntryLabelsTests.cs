using System.Text;
using System.Text.Json;
using Flare.Api.Loki;
using Xunit;

namespace Flare.Api.Tests.Loki;

public class LokiEntryLabelsTests
{
    private static LokiEntryPlan JsonPlan(Dictionary<string, string>? expressions = null) =>
        new([new LokiParserSpec(LokiParserKind.Json, expressions ?? new Dictionary<string, string>())], null);

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
            JsonPlan()).Parsed;

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
            JsonPlan(new Dictionary<string, string> { ["uid"] = "user.id", ["missing"] = "no.such" })).Parsed;

        Assert.Equal(new Dictionary<string, string> { ["uid"] = "42" }, parsed);
    }

    [Fact]
    public void Json_CollidingLabelsGetTheExtractedSuffix()
    {
        var parsed = LokiEntryLabels.Build(Row("""{"service_name":"x","trace_id":"y"}"""), JsonPlan()).Parsed;

        Assert.Equal("x", parsed["service_name_extracted"]);
        Assert.Equal("y", parsed["trace_id_extracted"]);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("{broken")]
    public void Json_NonObjectBodySetsTheParserError(string body)
    {
        var parsed = LokiEntryLabels.Build(Row(body), JsonPlan()).Parsed;

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

    // ---- logfmt, mixed parsers and line_format (ADR-0237) ----

    private static LokiParserSpec Json(params (string Label, string Path)[] expressions) =>
        new(LokiParserKind.Json, expressions.ToDictionary(e => e.Label, e => e.Path));

    private static LokiParserSpec Logfmt(params (string Label, string Key)[] expressions) =>
        new(LokiParserKind.Logfmt, expressions.ToDictionary(e => e.Label, e => e.Key));

    private static LokiEntryPlan Plan(string? lineFormat, params LokiParserSpec[] parsers) =>
        new(parsers, lineFormat is null ? null : LokiTemplate.Parse(lineFormat));

    [Fact]
    public void Logfmt_ExtractsEveryPair()
    {
        var parsed = LokiEntryLabels.Build(Row("""level=warn msg="slow query" took=1.5s"""), Plan(null, Logfmt())).Parsed;

        Assert.Equal(
            new Dictionary<string, string> { ["level"] = "warn", ["msg"] = "slow query", ["took"] = "1.5s" },
            parsed);
    }

    [Fact]
    public void Logfmt_ExplicitExpressionsExtractOnlyThoseKeys()
    {
        var parsed = LokiEntryLabels.Build(Row("level=warn http.status=503 other=x"), Plan(null, Logfmt(("code", "http.status"), ("gone", "nokey")))).Parsed;

        Assert.Equal(new Dictionary<string, string> { ["code"] = "503" }, parsed);
    }

    [Fact]
    public void Logfmt_CollidingLabelsAreSuffixedExtracted()
    {
        var entry = LokiEntryLabels.Build(Row("trace_id=zzz service_name=other k8s_pod_name=p detected_level=fake"), Plan(null, Logfmt()));

        Assert.Equal("zzz", entry.Parsed["trace_id_extracted"]);
        Assert.Equal("other", entry.Parsed["service_name_extracted"]);
        Assert.Equal("p", entry.Parsed["k8s_pod_name_extracted"]);
        Assert.Equal("fake", entry.Parsed["detected_level_extracted"]);
        Assert.Equal("abc123", entry.StructuredMetadata["trace_id"]);
    }

    [Fact]
    public void MixedParsers_AJsonLineIsParsedByJsonAndALogfmtLineByLogfmt()
    {
        var plan = Plan(null, Json(), Logfmt());

        var jsonLine = LokiEntryLabels.Build(Row("""{"status":500,"msg":"a b=c"}"""), plan).Parsed;
        var logfmtLine = LokiEntryLabels.Build(Row("status=200 msg=ok"), plan).Parsed;

        Assert.Equal(new Dictionary<string, string> { ["status"] = "500", ["msg"] = "a b=c" }, jsonLine);
        // The line is not JSON, so json reports an error; logfmt still extracts, as in a Drilldown "mixed" query.
        Assert.Equal("JSONParserErr", logfmtLine["__error__"]);
        Assert.Equal("200", logfmtLine["status"]);
        Assert.Equal("ok", logfmtLine["msg"]);
    }

    [Fact]
    public void MixedParsers_ALaterStageCannotOverwriteAnEarlierLabel()
    {
        var parsed = LokiEntryLabels.Build(Row("""{"user":"a"}"""), Plan(null, Json(), Json(("user2", "user"), ("user", "user")))).Parsed;

        Assert.Equal("a", parsed["user"]);
        Assert.Equal("a", parsed["user_extracted"]);
        Assert.Equal("a", parsed["user2"]);
    }

    [Fact]
    public void LineFormat_RewritesTheLineFromParsedLabels()
    {
        var entry = LokiEntryLabels.Build(Row("""{"msg":"disk full","code":28}"""), Plan("[{{.code}}] {{.msg | upper}}", Json()));

        Assert.Equal("[28] DISK FULL", entry.Line);
        // The labels are unchanged by line_format.
        Assert.Equal("disk full", entry.Parsed["msg"]);
    }

    [Fact]
    public void LineFormat_SeesMetadataAndStreamLabelsAndTheLevelFallback()
    {
        var entry = LokiEntryLabels.Build(Row("plain text"), Plan("{{.service_name}}/{{.k8s_pod_name}} {{.detected_level}} {{.level}} {{.trace_id}} {{.http_method}}"));

        Assert.Equal("api/web-1 error error abc123 GET", entry.Line);
    }

    [Fact]
    public void LineFormat_ParsedLabelsWinOverMetadata()
    {
        var entry = LokiEntryLabels.Build(Row("""{"level":"custom"}"""), Plan("{{.level}}", Json()));

        Assert.Equal("custom", entry.Line);
    }

    [Fact]
    public void LineFormat_LineAndTimestamp()
    {
        var entry = LokiEntryLabels.Build(Row("orig"), Plan("{{ __line__ }} @ {{ __timestamp__ }}"));

        Assert.Equal("orig @ 2025-10-09T08:53:20.1234567Z", entry.Line);
    }

    [Fact]
    public void NoLineFormat_KeepsTheBody()
    {
        Assert.Equal("hello", LokiEntryLabels.Build(Row("hello"), Plan(null, Logfmt())).Line);
    }

    [Fact]
    public void Flatten_ReportsTheKeyPathOfEveryLeaf()
    {
        var fields = LokiEntryLabels.TryFlattenJson("""{"a":{"b":{"c":1}},"d":"x","e":null}""")!;

        Assert.Equal([("a_b_c", "1", new[] { "a", "b", "c" }), ("d", "x", new[] { "d" })], fields.Select(f => (f.Name, f.Value, f.Path.ToArray())));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("\"str\"")]
    public void TryFlattenJson_IsNullForAnythingButAnObject(string body)
    {
        Assert.Null(LokiEntryLabels.TryFlattenJson(body));
    }

    [Fact]
    public void WriteStreams_Categorized_CarriesParsedLabelsOfBothParsers()
    {
        var entry = LokiEntryLabels.Build(Row("status=500 path=/x"), Plan(null, Json(), Logfmt()));

        using var doc = JsonDocument.Parse(Write([entry], categorize: true));
        var third = doc.RootElement.GetProperty("result")[0].GetProperty("values")[0][2];
        Assert.Equal("500", third.GetProperty("parsed").GetProperty("status").GetString());
        Assert.Equal("JSONParserErr", third.GetProperty("parsed").GetProperty("__error__").GetString());
    }

    [Fact]
    public void Json_ExplicitExpressionsAcceptBracketPathsAndArrayIndexes()
    {
        var parsed = LokiEntryLabels.Build(
            Row("""{"request":{"method":"GET","x.y":"dotted"},"servers":[{"name":"a"},{"name":"b"}]}"""),
            Plan(
                null,
                Json(("m", """["request"]["method"]"""), ("dotted", """request["x.y"]"""), ("second", "servers[1].name"), ("gone", "servers[5].name")))).Parsed;

        Assert.Equal(new Dictionary<string, string> { ["m"] = "GET", ["dotted"] = "dotted", ["second"] = "b" }, parsed);
    }

    // ---- ADR-0238 ----

    [Fact]
    public void StrictLogfmt_AMalformedLineKeepsTheLabelsBeforeTheFaultAndSetsTheError()
    {
        var plan = new LokiEntryPlan([new LokiParserSpec(LokiParserKind.Logfmt, new Dictionary<string, string>(), Strict: true)], null);

        var bad = LokiEntryLabels.Build(Row("level=info msg=\"unterminated code=5"), plan).Parsed;
        var good = LokiEntryLabels.Build(Row("level=info code=5"), plan).Parsed;

        Assert.Equal(new Dictionary<string, string> { ["level"] = "info", ["__error__"] = "LogfmtParserErr" }, bad);
        Assert.Equal(new Dictionary<string, string> { ["level"] = "info", ["code"] = "5" }, good);
    }

    [Fact]
    public void LenientLogfmt_NeverSetsTheError()
    {
        var plan = new LokiEntryPlan([new LokiParserSpec(LokiParserKind.Logfmt, new Dictionary<string, string>())], null);

        Assert.DoesNotContain("__error__", LokiEntryLabels.Build(Row("level=info msg=\"unterminated"), plan).Parsed.Keys);
    }

    private static LokiLogEntry Entry(long ns, string line, string service, string pod) =>
        LokiEntryLabels.Build(Row(line, resource: new() { ["service.name"] = service, ["k8s.pod.name"] = pod }) with { Ns = ns }, null);

    [Fact]
    public void TailFrame_WithoutDroppedEntriesHasNoDroppedKey()
    {
        using var frame = JsonDocument.Parse(LokiEndpoints.TailFrame([Entry(1_760_000_000_000_000_001, "a", "api", "p1")], []));

        Assert.Single(frame.RootElement.GetProperty("streams").EnumerateArray());
        Assert.False(frame.RootElement.TryGetProperty("dropped_entries", out _));
    }

    [Fact]
    public void TailFrame_ReportsEachDroppedEntryWithItsLabelsAndTimestamp()
    {
        var dropped = new[] { Entry(1_760_000_000_000_000_002, "lost one", "api", "p2"), Entry(1_760_000_000_000_000_003, "lost two", "api", "p3") };

        using var frame = JsonDocument.Parse(LokiEndpoints.TailFrame([], dropped));

        Assert.Empty(frame.RootElement.GetProperty("streams").EnumerateArray());
        var reported = frame.RootElement.GetProperty("dropped_entries").EnumerateArray().ToList();
        Assert.Equal(2, reported.Count);
        Assert.Equal("1760000000000000002", reported[0].GetProperty("timestamp").GetString());
        Assert.Equal("p2", reported[0].GetProperty("labels").GetProperty("k8s_pod_name").GetString());
        Assert.Equal("api", reported[1].GetProperty("labels").GetProperty("service_name").GetString());
        // The line itself is not repeated: only the labels and the time identify what was lost.
        Assert.DoesNotContain("lost one", System.Text.Encoding.UTF8.GetString(LokiEndpoints.TailFrame([], dropped)));
    }
}
