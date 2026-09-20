using Flare.Api.Caching;
using Flare.Api.Model;
using Xunit;

namespace Flare.Api.Tests.Caching;

public class QueryCacheKeyTests
{
    [Fact]
    public void Build_SameEndpointAndEquivalentRequest_ReturnsSameKey()
    {
        var a = new LogAggregateRequest { Filter = new LogFilter { Services = ["api"] }, BucketWidthSeconds = 60 };
        var b = new LogAggregateRequest { Filter = new LogFilter { Services = ["api"] }, BucketWidthSeconds = 60 };

        Assert.Equal(QueryCacheKey.Build("logs:aggregate", a), QueryCacheKey.Build("logs:aggregate", b));
    }

    [Fact]
    public void Build_DifferentRequestFields_ReturnsDifferentKeys()
    {
        var a = new LogAggregateRequest { Filter = new LogFilter { Services = ["api"] }, BucketWidthSeconds = 60 };
        var b = new LogAggregateRequest { Filter = new LogFilter { Services = ["worker"] }, BucketWidthSeconds = 60 };

        Assert.NotEqual(QueryCacheKey.Build("logs:aggregate", a), QueryCacheKey.Build("logs:aggregate", b));
    }

    [Fact]
    public void Build_DifferentEndpointSameRequest_ReturnsDifferentKeys()
    {
        var request = new LogAggregateRequest { Filter = new LogFilter(), BucketWidthSeconds = 60 };

        Assert.NotEqual(QueryCacheKey.Build("logs:aggregate", request), QueryCacheKey.Build("logs:search", request));
    }

    [Fact]
    public void Build_ReturnsKeyNamespacedUnderEndpoint()
    {
        var request = new LogAggregateRequest { Filter = new LogFilter(), BucketWidthSeconds = 60 };

        var key = QueryCacheKey.Build("logs:aggregate", request);

        Assert.StartsWith("flare:qcache:logs:aggregate:", key);
    }
}
