using System.Text.RegularExpressions;
using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class ExternalApiQueryBuilderTests
{
    private static readonly DateTimeOffset End = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    private static readonly ExternalDomainDetailRequest Detail = new() { Domain = "api.stripe.com" };

    [Theory]
    [InlineData(null, ExternalApiQueryBuilder.DefaultWindowMinutes)]
    [InlineData(0, ExternalApiQueryBuilder.DefaultWindowMinutes)]
    [InlineData(1, ExternalApiQueryBuilder.MinWindowMinutes)]
    [InlineData(15, 15)]
    [InlineData(100_000, ExternalApiQueryBuilder.MaxWindowMinutes)]
    public void ClampWindowMinutes_ClampsAndDefaults(int? requested, int expected)
    {
        Assert.Equal(expected, ExternalApiQueryBuilder.ClampWindowMinutes(requested));
    }

    [Fact]
    public void AttributeExprs_FallBackToOlderNames()
    {
        Assert.Equal(
            "multiIf(SpanAttributes['server.address'] != '', SpanAttributes['server.address'], " +
            "SpanAttributes['net.peer.name'] != '', SpanAttributes['net.peer.name'], " +
            "domain(if(SpanAttributes['url.full'] != '', SpanAttributes['url.full'], SpanAttributes['http.url'])))",
            ExternalApiQueryBuilder.DomainExpr);
        Assert.Contains("SpanAttributes['http.request.method'] != '', SpanAttributes['http.request.method'], SpanAttributes['http.method']", ExternalApiQueryBuilder.MethodExpr);
        Assert.Contains("SpanAttributes['http.response.status_code'] != '', SpanAttributes['http.response.status_code'], SpanAttributes['http.status_code']", ExternalApiQueryBuilder.StatusCodeExpr);
    }

    [Fact]
    public void EndpointExpr_PrefersUrlTemplate_ThenTemplatedPath_ThenRpc_ThenSpanName()
    {
        var expr = ExternalApiQueryBuilder.EndpointExpr;

        var template = expr.IndexOf("SpanAttributes['url.template'], ", StringComparison.Ordinal);
        var path = expr.IndexOf("arrayStringConcat(arrayMap(", StringComparison.Ordinal);
        var rpc = expr.IndexOf("concat(SpanAttributes['rpc.service'], '/', SpanAttributes['rpc.method'])", StringComparison.Ordinal);
        Assert.True(template >= 0 && template < path && path < rpc);
        Assert.EndsWith("Name)", expr);
        Assert.Contains("match(s, {idPattern:String}) OR (length(s) >= 16 AND match(s, '[0-9]')), {idPlaceholder:String}, s)", expr);

        // Same order, as ExternalEndpointSource ordinals.
        Assert.Equal(
            "multiIf(SpanAttributes['url.template'] != '', 0, " +
            "if(SpanAttributes['url.full'] != '', SpanAttributes['url.full'], SpanAttributes['http.url']) != '', 1, " +
            "SpanAttributes['rpc.method'] != '', 2, 3)",
            ExternalApiQueryBuilder.EndpointSourceExpr);
        Assert.Equal(0, (int)ExternalEndpointSource.UrlTemplate);
        Assert.Equal(1, (int)ExternalEndpointSource.UrlPath);
        Assert.Equal(2, (int)ExternalEndpointSource.Rpc);
        Assert.Equal(3, (int)ExternalEndpointSource.SpanName);
    }

    [Theory]
    [InlineData("12345", true)]
    [InlineData("3f2504e0-4f89-11d3-9a0c-0305e82c3301", true)]
    [InlineData("deadbeefdeadbeef", true)]
    [InlineData("v1", false)]
    [InlineData("customers", false)]
    [InlineData("abc123", false)]
    [InlineData("", false)]
    public void IdSegmentPattern_MatchesIdLikeSegmentsOnly(string segment, bool expected)
    {
        // .NET and RE2 agree on this pattern's syntax (no lookarounds or backreferences).
        Assert.Equal(expected, Regex.IsMatch(segment, ExternalApiQueryBuilder.IdSegmentPattern));
    }

    [Fact]
    public void BuildDomains_GroupsOutboundClientCallsByDomain()
    {
        var result = ExternalApiQueryBuilder.BuildDomains(new ExternalDomainsRequest(), 60, End);

        Assert.Contains(ExternalApiQueryBuilder.OutboundCallCondition, result.Sql);
        Assert.Contains($"{ExternalApiQueryBuilder.DomainExpr} != ''", result.Sql);
        Assert.Contains("mapContains(SpanAttributes, 'server.address') OR mapContains(SpanAttributes, 'net.peer.name')", result.Sql);
        Assert.Contains($"{ExternalApiQueryBuilder.DomainExpr} AS ExtDomain", result.Sql);
        Assert.Contains("quantiles(0.5, 0.95, 0.99)(DurationNano) AS Quantiles", result.Sql);
        Assert.Contains("uniqExact(ExtMethod, ExtEndpoint) AS EndpointCount", result.Sql);
        Assert.Contains("toUnixTimestamp64Milli(max(StartTime)) AS LastSeenUnixMs", result.Sql);
        Assert.Contains("GROUP BY ExtDomain", result.Sql);
        Assert.Contains($"{ExternalApiQueryBuilder.PortExpr} AS ExtPort", result.Sql);
        Assert.Contains($"arrayStringConcat(arraySort(p -> toUInt32OrZero(p), groupUniqArrayIf({ExternalApiQueryBuilder.MaxPortsPerDomain})(ExtPort, ExtPort != '')), ', ') AS Ports", result.Sql);
        Assert.DoesNotContain("{service:String}", result.Sql);

        var parameters = result.Parameters.ToDictionary();
        Assert.Equal(End.AddMinutes(-60).UtcDateTime, parameters["from"]);
        Assert.Equal(End.UtcDateTime, parameters["to"]);
        Assert.Equal(ExternalApiQueryBuilder.IdSegmentPattern, parameters["idPattern"]);
        Assert.Equal(ExternalApiQueryBuilder.IdPlaceholder, parameters["idPlaceholder"]);
        Assert.Equal((uint)(ExternalApiQueryBuilder.MaxRows + 1), parameters["limit"]);
    }

    [Fact]
    public void BuildDomains_ServiceFilterNarrowsToTheCaller()
    {
        var result = ExternalApiQueryBuilder.BuildDomains(new ExternalDomainsRequest { Service = "checkout" }, 60, End);

        Assert.Contains("ServiceName = {service:String}", result.Sql);
        Assert.Equal("checkout", result.Parameters.ToDictionary()["service"]);
    }

    [Fact]
    public void BuildFacets_IgnoresServiceFilter_AndSkipsEndpointTemplating()
    {
        var result = ExternalApiQueryBuilder.BuildFacets(60, End);

        Assert.Contains("arraySort(groupUniqArray(1000)(ServiceName)) AS Services", result.Sql);
        Assert.DoesNotContain("{service:String}", result.Sql);
        Assert.DoesNotContain("ExtEndpoint", result.Sql);
    }

    [Fact]
    public void DetailQueries_FilterToTheDomain_AndOptionalService()
    {
        var request = Detail with { Service = "checkout" };
        foreach (var built in new[]
        {
            ExternalApiQueryBuilder.BuildEndpoints(request, 60, End),
            ExternalApiQueryBuilder.BuildStatusCodes(request, 60, End),
            ExternalApiQueryBuilder.BuildCallers(request, 60, End),
            ExternalApiQueryBuilder.BuildTopErrors(request, 60, End),
            ExternalApiQueryBuilder.BuildSeries(request, 60, 60, End),
        })
        {
            Assert.Contains($"{ExternalApiQueryBuilder.DomainExpr} = {{domain:String}}", built.Sql);
            Assert.Contains("ServiceName = {service:String}", built.Sql);
            var parameters = built.Parameters.ToDictionary();
            Assert.Equal("api.stripe.com", parameters["domain"]);
            Assert.Equal("checkout", parameters["service"]);
        }
    }

    [Fact]
    public void BuildEndpoints_GroupsByMethodEndpointAndSource()
    {
        var result = ExternalApiQueryBuilder.BuildEndpoints(Detail, 60, End);

        Assert.Contains($"{ExternalApiQueryBuilder.EndpointExpr} AS ExtEndpoint", result.Sql);
        Assert.Contains($"{ExternalApiQueryBuilder.EndpointSourceExpr} AS ExtEndpointSource", result.Sql);
        Assert.Contains("GROUP BY ExtMethod, ExtEndpoint, ExtEndpointSource", result.Sql);
        Assert.Contains("idPattern", result.Parameters.ToDictionary().Keys);
    }

    [Fact]
    public void BuildStatusCodes_SkipsSpansWithoutOne_NumericOrder()
    {
        var result = ExternalApiQueryBuilder.BuildStatusCodes(Detail, 60, End);

        Assert.Contains("WHERE ExtStatusCode != ''", result.Sql);
        Assert.Contains("ORDER BY toUInt16OrNull(ExtStatusCode) ASC NULLS LAST", result.Sql);
        Assert.DoesNotContain("ExtEndpoint", result.Sql);
    }

    [Fact]
    public void BuildCallers_GroupsByService()
    {
        var result = ExternalApiQueryBuilder.BuildCallers(Detail, 60, End);

        Assert.Contains("GROUP BY ServiceName", result.Sql);
        Assert.Contains("countIf(IsError) AS ErrorCount", result.Sql);
    }

    [Fact]
    public void BuildTopErrors_ErrorSpansOnly_GroupedByWhatFailed_Capped()
    {
        var result = ExternalApiQueryBuilder.BuildTopErrors(Detail, 60, End);

        Assert.Contains("WHERE IsError", result.Sql);
        Assert.Contains("GROUP BY ExtMethod, ExtEndpoint, ExtEndpointSource, ExtStatusCode, ExtErrorType", result.Sql);
        Assert.Contains("SpanAttributes['error.type'] AS ExtErrorType", result.Sql);
        Assert.Contains("anyIf(StatusMessage, StatusMessage != '') AS SampleMessage", result.Sql);
        Assert.Equal((uint)ExternalApiQueryBuilder.MaxErrorGroups, result.Parameters.ToDictionary()["limit"]);
    }

    [Fact]
    public void PortExpr_FallsBackToOlderName_ThenUrlPort_ThenSchemeDefault()
    {
        var expr = ExternalApiQueryBuilder.PortExpr;

        var server = expr.IndexOf("SpanAttributes['server.port']", StringComparison.Ordinal);
        var net = expr.IndexOf("SpanAttributes['net.peer.port']", StringComparison.Ordinal);
        var url = expr.IndexOf("port(if(", StringComparison.Ordinal);
        var https = expr.IndexOf("= 'https', '443'", StringComparison.Ordinal);
        var http = expr.IndexOf("= 'http', '80'", StringComparison.Ordinal);
        Assert.True(server >= 0 && server < net && net < url && url < https && https < http);
        Assert.EndsWith(", '')", expr);
    }

    [Theory]
    [InlineData(5, 10)]
    [InlineData(15, 20)]
    [InlineData(60, 60)]
    [InlineData(360, 360)]
    [InlineData(1440, 1440)]
    public void BucketWidthSecondsFor_TargetsAboutSixtyBuckets_InTenSecondSteps(int windowMinutes, int expected)
    {
        Assert.Equal(expected, ExternalApiQueryBuilder.BucketWidthSecondsFor(windowMinutes));
    }

    [Fact]
    public void BuildSeries_BucketsCallsErrorsAndP95_OldestFirst()
    {
        var result = ExternalApiQueryBuilder.BuildSeries(Detail, 60, 60, End);

        Assert.Contains("toStartOfInterval(toDateTime(StartTime), INTERVAL {bucketWidth:UInt32} SECOND)", result.Sql);
        Assert.Contains("quantile(0.95)(DurationNano) AS P95", result.Sql);
        Assert.Contains("GROUP BY BucketStartUnixMs", result.Sql);
        Assert.Contains("ORDER BY BucketStartUnixMs", result.Sql);
        Assert.DoesNotContain("ExtEndpoint", result.Sql);
        Assert.Equal(60u, result.Parameters.ToDictionary()["bucketWidth"]);
    }
}
