using Flare.Api.Ai;
using Flare.Api.Model;
using Xunit;

namespace Flare.Api.Tests.Ai;

public class NlFilterParserTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    private static NlFilterResponse Logs(string json) => Parse(NlFilterParser.Logs, json);

    private static NlFilterResponse Traces(string json) => Parse(NlFilterParser.Traces, json);

    private static NlFilterResponse Parse(string target, string json)
    {
        var (result, error) = NlFilterParser.Parse(target, json, "m", Now);
        Assert.Null(error);
        return result!;
    }

    [Fact]
    public void ParsesTheRoadmapExample()
    {
        var r = Logs("""
            {"timeRange":"1h","services":["checkout"],"minSeverity":"error",
             "attributes":[{"bag":"log","key":"http.response.status_code","operator":"GreaterThanOrEqual","value":"500"},
                           {"bag":"log","key":"http.route","operator":"NotEquals","value":"/health"}]}
            """);

        Assert.Equal("1h", r.TimeRangePreset);
        Assert.Equal(["checkout"], r.Services);
        Assert.Equal(Enumerable.Range(17, 8), r.SeverityNumbers);
        Assert.Equal(2, r.AttributeFilters.Count);
        Assert.Equal("GreaterThanOrEqual", r.AttributeFilters[0].Operator);
        Assert.Equal("Log", r.AttributeFilters[0].Bag);
        Assert.Empty(r.Warnings);
    }

    [Fact]
    public void StripsMarkdownFence()
    {
        Assert.Equal(["a"], Logs("```json\n{\"services\":[\"a\"]}\n```").Services);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("")]
    public void RejectsNonObjects(string text)
    {
        var (result, error) = NlFilterParser.Parse(NlFilterParser.Logs, text, "m", Now);
        Assert.Null(result);
        Assert.NotNull(error);
    }

    [Fact]
    public void UnknownPresetFallsBackWithWarning()
    {
        var r = Logs("""{"timeRange":"fortnight"}""");
        Assert.Equal("1h", r.TimeRangePreset);
        Assert.Single(r.Warnings);
    }

    [Fact]
    public void TracesRejectLogOnlyPresets()
    {
        Assert.Equal("1h", Traces("""{"timeRange":"today"}""").TimeRangePreset);
        Assert.Equal("today", Logs("""{"timeRange":"today"}""").TimeRangePreset);
    }

    [Fact]
    public void AcceptsBoundedCustomRange()
    {
        var r = Logs("""{"timeRange":{"from":"2026-10-03T00:00:00Z","to":"2026-10-04T00:00:00Z"}}""");
        Assert.Equal("custom", r.TimeRangePreset);
        Assert.Equal(new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero), r.CustomRange!.From);
    }

    [Theory]
    [InlineData("""{"timeRange":{"from":"2026-10-04T00:00:00Z","to":"2026-10-03T00:00:00Z"}}""")]
    [InlineData("""{"timeRange":{"from":"2020-01-01T00:00:00Z","to":"2026-10-04T00:00:00Z"}}""")]
    [InlineData("""{"timeRange":{"from":"2026-10-04T00:00:00Z","to":"2027-10-04T00:00:00Z"}}""")]
    public void RejectsBadCustomRange(string json)
    {
        var r = Logs(json);
        Assert.Equal("1h", r.TimeRangePreset);
        Assert.Null(r.CustomRange);
        Assert.NotEmpty(r.Warnings);
    }

    [Fact]
    public void DropsInvalidAttributes()
    {
        var r = Logs("""
            {"attributes":[
              {"key":"","operator":"Equals","value":"x"},
              {"key":"a","operator":"DropTable","value":"x"},
              {"key":"b","bag":"nope","value":"x"},
              {"key":"c","operator":"GreaterThan","value":"abc"},
              {"key":"d","operator":"Equals","value":""},
              {"key":"e","operator":"In","values":[]},
              {"key":"ok","operator":"In","values":["1","2"]},
              {"key":"present","operator":"Exists"}]}
            """);

        Assert.Equal(["ok", "present"], r.AttributeFilters.Select(a => a.Key));
        Assert.Equal(["1", "2"], r.AttributeFilters[0].Values);
        Assert.Equal(6, r.Warnings.Count);
    }

    [Fact]
    public void CapsAttributeCount()
    {
        var items = string.Join(",", Enumerable.Range(0, 12).Select(i => $$"""{"key":"k{{i}}","value":"v"}"""));
        var r = Logs($$"""{"attributes":[{{items}}]}""");
        Assert.Equal(8, r.AttributeFilters.Count);
    }

    [Fact]
    public void SeveritiesListAndUnknown()
    {
        var r = Logs("""{"severities":["warn","bogus"]}""");
        Assert.Equal([13, 14, 15, 16], r.SeverityNumbers);
        Assert.Single(r.Warnings);
    }

    [Fact]
    public void LogOnlyFieldsAreDroppedForTraces()
    {
        var r = Traces("""{"search":"boom","minSeverity":"error","statusCodes":["error","weird"]}""");
        Assert.Equal("", r.Search);
        Assert.Empty(r.SeverityNumbers);
        Assert.Equal(["STATUS_CODE_ERROR"], r.StatusCodes);
        Assert.Equal(3, r.Warnings.Count);
    }

    [Fact]
    public void TraceAttributesUseSpanBag()
    {
        var r = Traces("""{"attributes":[{"key":"http.route","value":"/x"}]}""");
        Assert.Equal("Span", r.AttributeFilters[0].Bag);
    }

    [Fact]
    public void BuildsValidStructure()
    {
        var r = Traces("""
            {"structure":{"conditions":[
              {"name":"a","serviceName":"checkout"},
              {"name":"B","serviceName":"payments","statusCode":"error","minDurationMs":500}],
             "expression":"A -> B"}}
            """);

        Assert.NotNull(r.Structure);
        Assert.Equal("STATUS_CODE_ERROR", r.Structure!.Conditions![1].StatusCode);
        Assert.Equal(500_000_000UL, r.Structure.Conditions[1].MinDurationNano);
        Assert.Empty(r.Warnings);
    }

    [Fact]
    public void InvalidStructureIsDroppedWithWarning()
    {
        var r = Traces("""{"structure":{"conditions":[{"name":"A","serviceName":"x"}],"expression":"NOT A"}}""");
        Assert.Null(r.Structure);
        Assert.Single(r.Warnings);
    }

    [Fact]
    public void StructureIgnoredForLogs()
    {
        Assert.Null(Logs("""{"structure":{"conditions":[{"name":"A","serviceName":"x"}],"expression":"A"}}""").Structure);
    }
}
