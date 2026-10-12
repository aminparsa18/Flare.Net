using Flare.Api.Hosting;
using Xunit;

namespace Flare.Api.Tests.Hosting;

public sealed class SecurityHeadersTests
{
    private static Dictionary<string, string> Build(SecurityHeadersOptions o, bool https) =>
        SecurityHeaders.Build(o, https).ToDictionary(h => h.Key, h => h.Value);

    [Fact]
    public void Http_HasBaselineHeadersButNoHsts()
    {
        var h = Build(new SecurityHeadersOptions(), https: false);
        Assert.Equal("nosniff", h["X-Content-Type-Options"]);
        Assert.Equal("no-referrer", h["Referrer-Policy"]);
        Assert.Equal("default-src 'none'; frame-ancestors 'none'", h["Content-Security-Policy"]);
        Assert.DoesNotContain("Strict-Transport-Security", h.Keys);
    }

    [Fact]
    public void Https_AddsHsts_WithOptionalSubDomains()
    {
        Assert.Equal("max-age=15552000", Build(new SecurityHeadersOptions(), true)["Strict-Transport-Security"]);
        Assert.Equal("max-age=60; includeSubDomains",
            Build(new SecurityHeadersOptions { HstsMaxAgeSeconds = 60, HstsIncludeSubDomains = true }, true)["Strict-Transport-Security"]);
    }

    [Fact]
    public void ZeroMaxAge_DisablesHsts() =>
        Assert.DoesNotContain("Strict-Transport-Security", Build(new SecurityHeadersOptions { HstsMaxAgeSeconds = 0 }, true).Keys);

    [Fact]
    public void FrameAncestors_IsConfigurable_AndBlankFallsBackToNone()
    {
        Assert.Contains("frame-ancestors 'self' https://wiki.example.com",
            Build(new SecurityHeadersOptions { FrameAncestors = "'self' https://wiki.example.com" }, false)["Content-Security-Policy"]);
        Assert.Contains("frame-ancestors 'none'",
            Build(new SecurityHeadersOptions { FrameAncestors = " " }, false)["Content-Security-Policy"]);
    }
}
