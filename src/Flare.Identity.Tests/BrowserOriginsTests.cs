using Flare.Identity;
using Flare.Identity.IngestKeys;
using Xunit;

namespace Flare.Identity.Tests;

public class BrowserOriginsTests
{
    [Theory]
    [InlineData("https://flare.example.com", "https://flare.example.com")]
    [InlineData("https://flare.example.com/", "https://flare.example.com")]
    [InlineData("  https://flare.example.com/  ", "https://flare.example.com")]
    [InlineData("https://example.com/flare", "https://example.com")]
    [InlineData("https://example.com/flare/logs?x=1#top", "https://example.com")]
    [InlineData("https://example.com?x=1", "https://example.com")]
    [InlineData("https://example.com:443/", "https://example.com")]
    [InlineData("http://example.com:80", "http://example.com")]
    [InlineData("https://example.com:8443/", "https://example.com:8443")]
    [InlineData("http://example.com:443", "http://example.com:443")]
    [InlineData("HTTPS://Flare.Example.COM/", "https://flare.example.com")]
    [InlineData("http://localhost:5173/", "http://localhost:5173")]
    [InlineData("http://127.0.0.1:7777", "http://127.0.0.1:7777")]
    [InlineData("http://[::1]:5173/", "http://[::1]:5173")]
    [InlineData("http://[2001:DB8::1]/", "http://[2001:db8::1]")]
    [InlineData("https://user:pass@example.com/", "https://example.com")]
    [InlineData("https://bücher.example/", "https://xn--bcher-kva.example")]
    public void TryNormalize_WithPathAllowed_ReducesToTheBrowserOrigin(string input, string expected)
    {
        Assert.True(BrowserOrigins.TryNormalize(input, allowPath: true, out var origin));
        Assert.Equal(expected, origin);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("*")]
    [InlineData("flare.example.com")]
    [InlineData("/flare")]
    [InlineData("ftp://example.com")]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    public void TryNormalize_RejectsAnythingThatIsNotAnAbsoluteHttpUrl(string? input)
    {
        Assert.False(BrowserOrigins.TryNormalize(input, allowPath: true, out _));
    }

    [Theory]
    [InlineData("https://example.com/flare")]
    [InlineData("https://example.com?x=1")]
    [InlineData("https://example.com#x")]
    [InlineData("https://example.com//")]
    public void TryNormalize_WithoutPath_RejectsAnythingPastTheAuthority(string input)
    {
        Assert.False(BrowserOrigins.TryNormalize(input, allowPath: false, out _));
    }

    [Fact]
    public void TryNormalize_WithoutPath_StillAcceptsASingleTrailingSlash()
    {
        Assert.True(BrowserOrigins.TryNormalize("https://example.com/", allowPath: false, out var origin));
        Assert.Equal("https://example.com", origin);
    }

    [Fact]
    public void NormalizeConfigured_SkipsBlanksAndDeduplicatesAfterNormalizing()
    {
        var origins = BrowserOrigins.NormalizeConfigured(
            ["https://flare.example.com/", "", null, "  ", "HTTPS://FLARE.example.com/flare", "http://localhost:5173"],
            "Cors:AllowedOrigins", allowWildcard: false);

        Assert.Equal(["https://flare.example.com", "http://localhost:5173"], origins);
    }

    [Fact]
    public void NormalizeConfigured_ReturnsEmpty_ForNull()
    {
        Assert.Empty(BrowserOrigins.NormalizeConfigured(null, "Cors:AllowedOrigins", allowWildcard: false));
    }

    [Fact]
    public void NormalizeConfigured_ThrowsNamingTheKeyAndIndex_ForAnInvalidEntry()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            BrowserOrigins.NormalizeConfigured(["https://ok.example.com", "flare.example.com"], "Cors:AllowedOrigins", allowWildcard: false));

        Assert.Contains("Cors:AllowedOrigins:1", ex.Message);
        Assert.Contains("flare.example.com", ex.Message);
    }

    [Fact]
    public void NormalizeConfigured_KeepsWildcard_OnlyWhenAllowed()
    {
        Assert.Equal(["*", "https://a.example"], BrowserOrigins.NormalizeConfigured(["*", "https://a.example/", "*"], "Otlp:AllowedOrigins", allowWildcard: true));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            BrowserOrigins.NormalizeConfigured(["*"], "Cors:AllowedOrigins", allowWildcard: false));
        Assert.Contains("Cors:AllowedOrigins:0", ex.Message);
    }

    [Fact]
    public void IngestKeyOrigins_NormalizeStored_DropsPathsAndSkipsJunk()
    {
        Assert.Equal(
            ["https://app.example.com", "http://localhost:5173"],
            IngestKeyOrigins.NormalizeStored(["https://App.example.com/", "https://app.example.com/x", "not an origin", "http://localhost:5173"]));
    }
}
