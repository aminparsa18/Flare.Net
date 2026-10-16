using Flare.Api.Loki;
using Xunit;

namespace Flare.Api.Tests.Loki;

public class LokiDetectedFieldsTests
{
    private static LokiLogRow Row(string body, Dictionary<string, string>? log = null, string trace = "") => new(
        Ns: 1_760_000_000_000_000_000,
        Body: body,
        ServiceName: "api",
        SeverityText: "INFO",
        SeverityNumber: 9,
        TraceId: trace,
        SpanId: "",
        ScopeName: "",
        ScopeVersion: "",
        ResourceAttributes: new Dictionary<string, string> { ["service.name"] = "api", ["k8s.pod.name"] = "web-1", ["host.name"] = "h1" },
        LogAttributes: log ?? new Dictionary<string, string>());

    private static LokiDetectedField Field(IReadOnlyList<LokiDetectedField> fields, string label) => fields.Single(f => f.Label == label);

    [Fact]
    public void JsonFields_AreReportedWithTheirParserTypeAndKeyPath()
    {
        var fields = LokiDetectedFields.Analyze(
            [
                Row("""{"status":200,"request":{"method":"GET"},"took":"12ms"}"""),
                Row("""{"status":500,"request":{"method":"POST"},"took":"1.5s"}"""),
                Row("""{"status":200,"request":{"method":"GET"},"took":"3ms"}"""),
            ],
            100);

        var status = Field(fields, "status");
        Assert.Equal(("int", 2UL), (status.Type, status.Cardinality));
        Assert.Equal(["json"], status.Parsers);
        Assert.Equal(["status"], status.JsonPath);
        var method = Field(fields, "request_method");
        Assert.Equal(("string", 2UL), (method.Type, method.Cardinality));
        Assert.Equal(["request", "method"], method.JsonPath);
        Assert.Equal("duration", Field(fields, "took").Type);
    }

    [Fact]
    public void LogfmtFields_HaveNoJsonPath()
    {
        var fields = LokiDetectedFields.Analyze([Row("level=info size=10KB ok=true ratio=0.5")], 100);

        Assert.Equal(["logfmt"], Field(fields, "size").Parsers);
        Assert.Null(Field(fields, "size").JsonPath);
        Assert.Equal("bytes", Field(fields, "size").Type);
        Assert.Equal("boolean", Field(fields, "ok").Type);
        Assert.Equal("float", Field(fields, "ratio").Type);
    }

    [Fact]
    public void AFieldInBothFormats_ListsBothParsers()
    {
        var fields = LokiDetectedFields.Analyze([Row("""{"user":"a"}"""), Row("user=b")], 100);

        var user = Field(fields, "user");
        Assert.Equal(["json", "logfmt"], user.Parsers);
        Assert.Equal(2UL, user.Cardinality);
    }

    [Fact]
    public void StructuredMetadata_NeedsNoParser()
    {
        var fields = LokiDetectedFields.Analyze(
            [Row("plain", new() { ["http.method"] = "GET" }, trace: "t1"), Row("plain", new() { ["http.method"] = "PUT" }, trace: "t2")],
            100);

        Assert.Null(Field(fields, "http_method").Parsers);
        Assert.Equal(2UL, Field(fields, "http_method").Cardinality);
        Assert.Equal(2UL, Field(fields, "trace_id").Cardinality);
        Assert.Equal(1UL, Field(fields, "detected_level").Cardinality);
        Assert.DoesNotContain(fields, f => f.Label == "service_name");
    }

    [Fact]
    public void AParsedFieldThatCollidesWithMetadataIsSuffixedExtracted()
    {
        var fields = LokiDetectedFields.Analyze([Row("""{"trace_id":"abc","detected_level":"x"}""", trace: "t1")], 100);

        Assert.Equal(["json"], Field(fields, "trace_id_extracted").Parsers);
        Assert.Null(Field(fields, "trace_id").Parsers);
        Assert.Contains(fields, f => f.Label == "detected_level_extracted");
    }

    [Fact]
    public void AJsonLineDoesNotYieldLogfmtFields()
    {
        var fields = LokiDetectedFields.Analyze([Row("""{"msg":"took a=1 b=2"}""")], 100);

        Assert.DoesNotContain(fields, f => f.Label is "a" or "b");
    }

    [Fact]
    public void MixedIntsAndFloats_AreFloat_AndAnythingElseMixedIsString()
    {
        var fields = LokiDetectedFields.Analyze(
            [Row("a=1 b=1 c=x"), Row("a=1.5 b=true c=2")],
            100);

        Assert.Equal("float", Field(fields, "a").Type);
        Assert.Equal("string", Field(fields, "b").Type);
        Assert.Equal("string", Field(fields, "c").Type);
    }

    [Fact]
    public void FieldLimit_KeepsTheFirstFieldsByName()
    {
        var fields = LokiDetectedFields.Analyze([Row("""{"z":1,"a":2,"m":3}""")], 2);

        Assert.Equal(2, fields.Count);
        Assert.True(string.CompareOrdinal(fields[0].Label, fields[1].Label) < 0);
    }

    [Fact]
    public void Cardinality_StopsGrowingAtTheCap()
    {
        var rows = Enumerable.Range(0, LokiDetectedFields.MaxDistinctValues + 50).Select(i => Row($"id={i}")).ToList();

        Assert.Equal((ulong)LokiDetectedFields.MaxDistinctValues, Field(LokiDetectedFields.Analyze(rows, 100), "id").Cardinality);
    }

    [Theory]
    [InlineData("true", "boolean")]
    [InlineData("false", "boolean")]
    [InlineData("42", "int")]
    [InlineData("-7", "int")]
    [InlineData("3.14", "float")]
    [InlineData("1e3", "float")]
    [InlineData("250ms", "duration")]
    [InlineData("1h30m", "duration")]
    [InlineData("1.5s", "duration")]
    [InlineData("10KB", "bytes")]
    [InlineData("1.5 MiB", "bytes")]
    [InlineData("2gb", "bytes")]
    [InlineData("10.0.0.1", "string")]
    [InlineData("NaN", "string")]
    [InlineData("hello", "string")]
    [InlineData("v1.2.3", "string")]
    public void TypeOf_ClassifiesSingleValues(string value, string expected)
    {
        Assert.Equal(expected, LokiDetectedFields.TypeOf(value));
    }
}

public class LokiPatternAssemblerTests
{
    [Fact]
    public void Assemble_UsesLokisPlaceholderAndTheLatestTemplateOfACluster()
    {
        var rows = new[]
        {
            new LokiPatternRow("p1", "info", "GET <*> 200", Bucket: 0, Count: 3),
            new LokiPatternRow("p1", "info", "GET <*> <*>", Bucket: 1, Count: 4),
        };

        var series = Assert.Single(LokiPatternAssembler.Assemble(rows, originMs: 1_000_000, stepMs: 60_000, limit: 10));

        Assert.Equal("GET <_> <_>", series.Pattern);
        Assert.Equal("info", series.Level);
        Assert.Equal([(1000L, 3L), (1060L, 4L)], series.Samples);
    }

    [Fact]
    public void Assemble_OneSeriesPerClusterAndLevel_BusiestFirst()
    {
        var rows = new[]
        {
            new LokiPatternRow("p1", "info", "a <*>", 0, 5),
            new LokiPatternRow("p1", "error", "a <*>", 0, 1),
            new LokiPatternRow("p2", "info", "b <*>", 0, 9),
        };

        var series = LokiPatternAssembler.Assemble(rows, 0, 1000, limit: 10);

        Assert.Equal([("b <_>", "info"), ("a <_>", "info"), ("a <_>", "error")], series.Select(s => (s.Pattern, s.Level)));
    }

    [Fact]
    public void Assemble_LimitKeepsTheBusiestSeries()
    {
        var rows = Enumerable.Range(0, 5).Select(i => new LokiPatternRow($"p{i}", "info", $"t{i} <*>", 0, i + 1)).ToList();

        var series = LokiPatternAssembler.Assemble(rows, 0, 1000, limit: 2);

        Assert.Equal(["t4 <_>", "t3 <_>"], series.Select(s => s.Pattern));
    }

    [Fact]
    public void Assemble_MergesRowsOfTheSameBucket()
    {
        var rows = new[]
        {
            new LokiPatternRow("p1", "info", "x <*>", 2, 3),
            new LokiPatternRow("p1", "info", "x <*>", 2, 4),
        };

        var series = Assert.Single(LokiPatternAssembler.Assemble(rows, 0, 10_000, 10));

        Assert.Equal([(20L, 7L)], series.Samples);
    }

    [Fact]
    public void Assemble_NoRowsNoSeries()
    {
        Assert.Empty(LokiPatternAssembler.Assemble([], 0, 1000, 10));
    }
}
