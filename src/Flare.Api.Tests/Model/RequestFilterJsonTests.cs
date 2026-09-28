using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using MemoryPack;
using Xunit;

namespace Flare.Api.Tests.Model;

/// <summary>
/// A request's <c>Filter</c>/<c>Condition</c> is never null after deserialization, even when
/// the body omits it or sends <c>null</c>. System.Text.Json assigns init-only properties
/// through the object initializer, so the <c>= new()</c> default alone is overwritten with
/// null. That used to reach <c>CachingLogQueryService</c> as a
/// <see cref="NullReferenceException"/> (a 500 for <c>POST /api/logs/search</c> with
/// <c>{"search":"x"}</c> instead of <c>{"filter":{"search":"x"}}</c>).
/// </summary>
public class RequestFilterJsonTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("""{"filter":null}""")]
    [InlineData("""{"search":"misplaced"}""")]
    public void LogSearchRequest_MissingOrNullFilter_IsEmptyFilter(string json)
    {
        var request = JsonSerializer.Deserialize(json, LogsJsonContext.Default.LogSearchRequest)!;

        Assert.NotNull(request.Filter);
        Assert.Null(request.Filter.Search);
    }

    [Fact]
    public void LogSearchRequest_PresentFilter_IsKept()
    {
        var request = JsonSerializer.Deserialize("""{"filter":{"search":"boom"}}""", LogsJsonContext.Default.LogSearchRequest)!;

        Assert.Equal("boom", request.Filter.Search);
    }

    [Fact]
    public void SpanSearchRequest_MissingFilter_IsEmptyFilter()
    {
        var request = JsonSerializer.Deserialize("{}", SpansJsonContext.Default.SpanSearchRequest)!;

        Assert.NotNull(request.Filter);
    }

    [Fact]
    public void AlertRuleRequest_MissingCondition_IsEmptyFilter()
    {
        // `required` members put this type on the source generator's constructor-style
        // converter (see AlertRuleRequest's remarks); the guard has to hold there too.
        const string json = """{"name":"x","threshold":{"count":1},"windowSeconds":300}""";

        var request = JsonSerializer.Deserialize(json, AlertsJsonContext.Default.AlertRuleRequest)!;

        Assert.NotNull(request.Condition);
    }

    [Fact]
    public void LogSearchRequest_MemoryPackRoundTrip_KeepsFilter()
    {
        var bytes = MemoryPackSerializer.Serialize(new LogSearchRequest { Filter = new LogFilter { Search = "boom" } });

        var request = MemoryPackSerializer.Deserialize<LogSearchRequest>(bytes)!;

        Assert.Equal("boom", request.Filter.Search);
    }
}
