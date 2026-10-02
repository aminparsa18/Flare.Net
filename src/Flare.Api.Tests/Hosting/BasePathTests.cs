using Flare.Api.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flare.Api.Tests.Hosting;

public sealed class BasePathTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("  ", "")]
    [InlineData("/", "")]
    [InlineData("flare", "/flare")]
    [InlineData("/flare", "/flare")]
    [InlineData("/flare/", "/flare")]
    [InlineData("/tools/flare.v2", "/tools/flare.v2")]
    public void Normalize_ProducesLeadingSlashNoTrailingSlash(string? raw, string expected) =>
        Assert.Equal(expected, BasePath.Normalize(raw));

    [Theory]
    [InlineData("/a//b")]
    [InlineData("/fla re")]
    [InlineData("/flare?x=1")]
    [InlineData("https://example.com/flare")]
    public void Normalize_RejectsValuesThatAreNotAPathPrefix(string raw) =>
        Assert.Throws<InvalidOperationException>(() => BasePath.Normalize(raw));

    [Theory]
    [InlineData("/flare/api/auth/me", "/flare", "/api/auth/me")]   // proxy kept the prefix
    [InlineData("/api/auth/me", "/flare", "/api/auth/me")]          // proxy stripped it
    [InlineData("/flare", "/flare", "")]
    public async Task UseFlareBasePath_SplitsPrefixOrAssumesItWasStripped(string requestPath, string expectedPathBase, string expectedPath)
    {
        var (pathBase, path) = await Run("/flare", requestPath);

        Assert.Equal(expectedPathBase, pathBase);
        Assert.Equal(expectedPath, path);
    }

    [Fact]
    public async Task UseFlareBasePath_IsANoOpWhenUnset()
    {
        var (pathBase, path) = await Run("", "/api/auth/me");

        Assert.Equal("", pathBase);
        Assert.Equal("/api/auth/me", path);
    }

    [Fact]
    public async Task UseFlareBasePath_DoesNotMatchAPartialSegment()
    {
        // "/flare-other/x" is not under "/flare" - it must not be split mid-segment.
        var (pathBase, path) = await Run("/flare", "/flare-other/x");

        Assert.Equal("/flare", pathBase);
        Assert.Equal("/flare-other/x", path);
    }

    private static async Task<(string PathBase, string Path)> Run(string basePath, string requestPath)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = requestPath;

        var app = new ApplicationBuilder(new ServiceCollection().BuildServiceProvider());
        app.UseFlareBasePath(basePath);
        await app.Build()(context);

        return (context.Request.PathBase.Value ?? "", context.Request.Path.Value ?? "");
    }
}
