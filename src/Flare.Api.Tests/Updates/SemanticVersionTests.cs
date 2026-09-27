using Flare.Api.Updates;
using Xunit;

namespace Flare.Api.Tests.Updates;

/// <summary>
/// Covers <see cref="SemanticVersion"/> - the pure half of the update check (ADR-0068).
/// <see cref="ReleaseCheckService"/> itself calls GitHub, so it's verified against the real
/// API instead, same convention as every other network-bound service in this project.
/// </summary>
public class SemanticVersionTests
{
    [Theory]
    [InlineData("0.5.1", 0, 5, 1, null)]
    [InlineData("v0.5.1", 0, 5, 1, null)]
    [InlineData("1.2.3-rc.1", 1, 2, 3, "rc.1")]
    [InlineData("1.2.3+abc123", 1, 2, 3, null)]
    [InlineData("v10.0.0-beta+sha.1", 10, 0, 0, "beta")]
    public void TryParse_AcceptsVersions(string text, int major, int minor, int patch, string? prerelease)
    {
        Assert.True(SemanticVersion.TryParse(text, out var version));
        Assert.Equal(new SemanticVersion(major, minor, patch, prerelease), version);
    }

    [Theory]
    [InlineData("dev")]
    [InlineData("edge")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("flare-cli-v0.1.6")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3-rc..1")]
    [InlineData("-1.2.3")]
    public void TryParse_RejectsNonVersions(string? text)
    {
        Assert.False(SemanticVersion.TryParse(text, out _));
    }

    [Theory]
    // SemVer 2.0.0 section 11's own example ordering, plus the numeric-not-lexical cases.
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1")]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.beta")]
    [InlineData("1.0.0-alpha.beta", "1.0.0-beta")]
    [InlineData("1.0.0-beta", "1.0.0-beta.2")]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.11")]
    [InlineData("1.0.0-beta.11", "1.0.0-rc.1")]
    [InlineData("1.0.0-rc.1", "1.0.0")]
    [InlineData("0.5.1", "0.6.0")]
    [InlineData("0.9.0", "0.10.0")]
    [InlineData("0.10.9", "1.0.0")]
    public void CompareTo_OrdersLowerBeforeHigher(string lower, string higher)
    {
        Assert.True(SemanticVersion.TryParse(lower, out var a));
        Assert.True(SemanticVersion.TryParse(higher, out var b));

        Assert.True(a.CompareTo(b) < 0);
        Assert.True(b.CompareTo(a) > 0);
    }

    [Fact]
    public void CompareTo_IgnoresBuildMetadataAndPrefix()
    {
        Assert.True(SemanticVersion.TryParse("v1.2.3", out var a));
        Assert.True(SemanticVersion.TryParse("1.2.3+deadbeef", out var b));

        Assert.Equal(0, a.CompareTo(b));
    }

    [Fact]
    public void HighestStableTag_SkipsPrereleasesAndOtherTagFamilies()
    {
        var best = SemanticVersion.HighestStableTag(
            ["v0.5.1", "flare-cli-v0.1.6", "v0.10.0", "v0.11.0-rc.1", "v0.9.0", "hosting-v9.9.9"]);

        Assert.NotNull(best);
        Assert.Equal("v0.10.0", best.Value.Tag);
        Assert.Equal("0.10.0", best.Value.Version.ToString());
    }

    [Fact]
    public void HighestStableTag_NullWhenNoVersionTags()
    {
        Assert.Null(SemanticVersion.HighestStableTag(["flare-cli-v0.1.6", "latest"]));
    }
}
