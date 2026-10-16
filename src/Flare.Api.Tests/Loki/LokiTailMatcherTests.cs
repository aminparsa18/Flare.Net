using Flare.Api.LiveTail;
using Flare.Api.Loki;
using Flare.Api.Model;
using Xunit;

namespace Flare.Api.Tests.Loki;

public class LokiTailMatcherTests
{
    private static readonly LiveTailOptions Options = new();

    private static LokiLogRow Row(
        string body = "hello", string service = "api", byte severity = 9, Dictionary<string, string>? resource = null, Dictionary<string, string>? log = null, string trace = "") => new(
        Ns: 1_760_000_000_123_456_789,
        Body: body,
        ServiceName: service,
        SeverityText: "INFO",
        SeverityNumber: severity,
        TraceId: trace,
        SpanId: "",
        ScopeName: "My.Scope",
        ScopeVersion: "",
        ResourceAttributes: resource ?? new() { ["service.name"] = service, ["k8s.pod.name"] = "web-1", ["host.name"] = "h1" },
        LogAttributes: log ?? new() { ["http.method"] = "GET" });

    private static LokiTailMatcher Compile(string query) =>
        LokiTailMatcher.Compile(LokiQlParser.ParseSelector(query), Options);

    private static LokiLogEntry? Match(string query, LokiLogRow row) =>
        Compile(query).TryMatch(row, out var entry) ? entry : null;

    [Fact]
    public void StreamMatchers_CoverStreamMetadataAndAttributeLabels()
    {
        Assert.NotNull(Match("""{service_name="api", k8s_pod_name="web-1", http_method="GET", scope_name="My.Scope"}""", Row()));
        Assert.Null(Match("""{service_name="web"}""", Row()));
        Assert.Null(Match("""{http_method="POST"}""", Row()));
        Assert.NotNull(Match("""{detected_level="info"}""", Row()));
        Assert.Null(Match("""{detected_level="error"}""", Row()));
    }

    [Fact]
    public void LabelRegexes_AreAnchored()
    {
        Assert.NotNull(Match("""{k8s_pod_name=~"web-.*"}""", Row()));
        Assert.Null(Match("""{k8s_pod_name=~"web"}""", Row()));
        Assert.NotNull(Match("""{k8s_pod_name!~"db-.*"}""", Row()));
        Assert.Null(Match("""{k8s_pod_name!~"web-.*"}""", Row()));
    }

    [Fact]
    public void AnAbsentLabelIsTheEmptyString()
    {
        Assert.NotNull(Match("""{service_name="api", nothing=""}""", Row()));
        Assert.Null(Match("""{service_name="api", nothing!=""}""", Row()));
        Assert.NotNull(Match("""{service_name="api", nothing!~".+"}""", Row()));
    }

    [Fact]
    public void LineFilters_AreCaseSensitiveContainsAndUnanchoredRegex()
    {
        var row = Row("Connection refused to db-1");

        Assert.NotNull(Match("""{service_name="api"} |= "refused" """, row));
        Assert.Null(Match("""{service_name="api"} |= "Refused" """, row));
        Assert.NotNull(Match("""{service_name="api"} |= "timeout" or "refused" """, row));
        Assert.Null(Match("""{service_name="api"} != "refused" """, row));
        Assert.NotNull(Match("""{service_name="api"} != "timeout" != "x" """, row));
        Assert.NotNull(Match("""{service_name="api"} |~ "db-\d" """, row));
        Assert.Null(Match("""{service_name="api"} !~ "db-\d" """, row));
        Assert.NotNull(Match("""{service_name="api"} |= "" """, row));
    }

    [Fact]
    public void PatternFilters()
    {
        var row = Row("GET /x 200 took 5ms");

        Assert.NotNull(Match("""{service_name="api"} |> "GET <_> 200 <_>" """, row));
        Assert.Null(Match("""{service_name="api"} |> "POST <_>" """, row));
        Assert.Null(Match("""{service_name="api"} !> "GET <_>" """, row));
    }

    [Fact]
    public void Json_LabelFiltersSeeParsedFields_AndNumbersCompareNumerically()
    {
        var row = Row("""{"status":503,"request":{"method":"GET"},"msg":"down"}""");

        Assert.NotNull(Match("""{service_name="api"} | json | status >= 500 and request_method="GET" """, row));
        Assert.Null(Match("""{service_name="api"} | json | status < 500 """, row));
        Assert.NotNull(Match("""{service_name="api"} | json | status == 503 """, row));
        Assert.NotNull(Match("""{service_name="api"} | json | status!=200 """, row));
        Assert.NotNull(Match("""{service_name="api"} | json | msg=~"d.*" or status=1 """, row));
    }

    [Fact]
    public void ANonNumericValueFailsNumericComparisonsExceptNotEqual()
    {
        var row = Row("""{"status":"oops"}""");

        Assert.Null(Match("""{service_name="api"} | json | status > 1 """, row));
        Assert.NotNull(Match("""{service_name="api"} | json | status != 1 """, row));
    }

    [Fact]
    public void Logfmt_ParsesPairs()
    {
        var row = Row("""level=error msg="db down" attempts=3""");

        var entry = Match("""{service_name="api"} | logfmt | attempts > 2 | level="error" """, row);

        Assert.NotNull(entry);
        Assert.Equal("db down", entry.Parsed["msg"]);
        Assert.Null(Match("""{service_name="api"} | logfmt | attempts > 5""", row));
    }

    [Fact]
    public void MixedParsers_AndTheDrilldownDropStage()
    {
        var query = """{service_name="api"} | json | logfmt | drop __error__, __error_details__ | status="500" """;

        Assert.NotNull(Match(query, Row("""{"status":500}""")));
        Assert.NotNull(Match(query, Row("status=500")));
        Assert.Null(Match(query, Row("status=200")));
    }

    [Fact]
    public void ParseErrors_AreVisibleToLabelFilters()
    {
        Assert.NotNull(Match("""{service_name="api"} | json | __error__="JSONParserErr" """, Row("not json")));
        Assert.Null(Match("""{service_name="api"} | json | __error__="" """, Row("not json")));
        Assert.NotNull(Match("""{service_name="api"} | json | __error__="" """, Row("""{"a":1}""")));
    }

    [Fact]
    public void Level_IsTheSeverityUntilAParserFindsOne()
    {
        Assert.NotNull(Match("""{service_name="api"} | level="info" """, Row("x", severity: 9)));
        Assert.NotNull(Match("""{service_name="api"} | json | level="custom" """, Row("""{"level":"custom"}""")));
        Assert.NotNull(Match("""{service_name="api"} | json | level="info" """, Row("""{"a":1}""", severity: 10)));
    }

    [Fact]
    public void LineFormat_RewritesTheDeliveredLine_AlsoAsTheLastStage()
    {
        var entry = Match("""{service_name="api"} | json | status >= 500 | line_format "{{.status}}: {{.msg}}" """, Row("""{"status":503,"msg":"down"}"""));

        Assert.NotNull(entry);
        Assert.Equal("503: down", entry.Line);
        Assert.Equal("503", entry.Parsed["status"]);
    }

    [Fact]
    public void LabelFiltersAfterLineFormatStillFilter()
    {
        var query = """{service_name="api"} | json | line_format "{{.msg}}" | status="500" """;

        Assert.NotNull(Match(query, Row("""{"status":500,"msg":"x"}""")));
        Assert.Null(Match(query, Row("""{"status":200,"msg":"x"}""")));
    }

    [Fact]
    public void Entry_CarriesTheSameLabelsAsAQueryResponse()
    {
        var row = Row("hello", trace: "t1");
        var entry = Match("""{service_name="api"}""", row)!;
        var expected = LokiEntryLabels.Build(row);

        Assert.Equal(expected.StreamLabels, entry.StreamLabels);
        Assert.Equal(expected.StructuredMetadata, entry.StructuredMetadata);
        Assert.Equal("hello", entry.Line);
    }

    [Fact]
    public void Prefilter_NarrowsOnServiceAndLevelEquality()
    {
        var filter = Compile("""{service_name="api", detected_level="error"} |= "x" """).Prefilter;

        Assert.Equal(["api"], filter.Services);
        Assert.Equal<byte>([17, 18, 19, 20], filter.SeverityNumbers!);
    }

    [Fact]
    public void Prefilter_IsEmptyForQueriesItCannotNarrow()
    {
        var filter = Compile("""{service_name=~"a|b", k8s_pod_name="x"}""").Prefilter;

        Assert.Null(filter.Services);
        Assert.Null(filter.SeverityNumbers);
    }

    [Fact]
    public void Prefilter_NeverExcludesALineTheQueryMatches()
    {
        var matcher = Compile("""{service_name="api", detected_level="warn"} |= "slow" """);
        var row = Row("slow query", severity: 13);

        Assert.True(matcher.TryMatch(row, out _));
        Assert.Contains(row.SeverityNumber, matcher.Prefilter.SeverityNumbers!);
        Assert.Contains(row.ServiceName, matcher.Prefilter.Services!);
    }

    [Theory]
    [InlineData("""{a="b"} |~ "(?=x)y" """, "linear-time")]
    [InlineData("""{a=~"(x)\1"}""", "linear-time")]
    public void RegexesTheLinearTimeEngineCannotRun_AreRefusedWithTheirPattern(string query, string mention)
    {
        var ex = Assert.Throws<LokiException>(() => Compile(query));

        Assert.Contains(mention, ex.Message);
    }

    [Fact]
    public void RegexLimits_FollowLiveTailsOptions()
    {
        var tight = new LiveTailOptions { MaxRegexPatterns = 1, MaxRegexPatternLength = 8 };

        Assert.Throws<LokiException>(() => LokiTailMatcher.Compile(LokiQlParser.ParseSelector("""{a=~"x"} |~ "y" """), tight));
        Assert.Throws<LokiException>(() => LokiTailMatcher.Compile(LokiQlParser.ParseSelector("""{a="b"} |~ "this is far too long" """), tight));
    }

    [Fact]
    public void ToRow_ConvertsALogEvent()
    {
        var timestamp = new DateTimeOffset(2026, 10, 16, 12, 0, 0, 500, TimeSpan.Zero);
        var dto = new LogEventDto
        {
            EventId = Guid.NewGuid(),
            Timestamp = timestamp,
            ObservedTimestamp = timestamp,
            IngestedAt = timestamp,
            TraceId = "t",
            SpanId = "s",
            TraceFlags = 0,
            SeverityText = "WARN",
            SeverityNumber = 13,
            ServiceName = "api",
            Body = "b",
            ResourceSchemaUrl = "",
            ResourceAttributes = new Dictionary<string, string> { ["k"] = "v" },
            ScopeSchemaUrl = "",
            ScopeName = "scope",
            ScopeVersion = "1",
            ScopeAttributes = new Dictionary<string, string>(),
            LogAttributes = new Dictionary<string, string> { ["a"] = "b" },
            EventName = "",
            PatternId = "",
            PatternTemplate = "",
        };

        var row = LokiTailMatcher.ToRow(dto);

        Assert.Equal(new DateTimeOffset(2026, 10, 16, 12, 0, 0, 500, TimeSpan.Zero).ToUnixTimeMilliseconds() * 1_000_000, row.Ns);
        Assert.Equal(("api", "b", (byte)13, "t", "s"), (row.ServiceName, row.Body, row.SeverityNumber, row.TraceId, row.SpanId));
    }
}
