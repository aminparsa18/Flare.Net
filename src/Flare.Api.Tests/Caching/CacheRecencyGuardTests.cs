using Flare.Api.Caching;
using Xunit;

namespace Flare.Api.Tests.Caching;

public class CacheRecencyGuardTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan RecentWindow = TimeSpan.FromMinutes(2);

    [Fact]
    public void IsCacheable_NullTo_ReturnsFalse()
    {
        Assert.False(CacheRecencyGuard.IsCacheable(null, Now, RecentWindow));
    }

    [Fact]
    public void IsCacheable_ToWithinRecentWindow_ReturnsFalse()
    {
        var to = Now - TimeSpan.FromMinutes(1);

        Assert.False(CacheRecencyGuard.IsCacheable(to, Now, RecentWindow));
    }

    [Fact]
    public void IsCacheable_ToExactlyAtRecentWindowBoundary_ReturnsTrue()
    {
        var to = Now - RecentWindow;

        Assert.True(CacheRecencyGuard.IsCacheable(to, Now, RecentWindow));
    }

    [Fact]
    public void IsCacheable_ToWellInThePast_ReturnsTrue()
    {
        var to = Now - TimeSpan.FromHours(1);

        Assert.True(CacheRecencyGuard.IsCacheable(to, Now, RecentWindow));
    }

    [Fact]
    public void IsCacheable_ToInTheFuture_ReturnsFalse()
    {
        var to = Now + TimeSpan.FromMinutes(5);

        Assert.False(CacheRecencyGuard.IsCacheable(to, Now, RecentWindow));
    }
}
