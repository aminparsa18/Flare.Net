using Xunit;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Tests.Status;

public class StatusBrandingTests
{
    [Theory]
    [InlineData("status.acme.com", true)]
    [InlineData("a-b.example.co.uk", true)]
    [InlineData("localhost", false)]
    [InlineData("Status.Acme.com", false)]
    [InlineData("status.acme.com:8443", false)]
    [InlineData("https://status.acme.com", false)]
    [InlineData("status.acme.com/path", false)]
    [InlineData("-bad.acme.com", false)]
    [InlineData("bad..acme.com", false)]
    [InlineData("10.0.0.1", false)]
    [InlineData("", false)]
    public void IsValidDomain(string domain, bool expected) => Assert.Equal(expected, StatusPageRequest.IsValidDomain(domain));

    [Theory]
    [InlineData("#1a73e8", true)]
    [InlineData("#1A73E8", true)]
    [InlineData("1a73e8", false)]
    [InlineData("#fff", false)]
    [InlineData("#gggggg", false)]
    public void IsValidAccentColor(string color, bool expected) => Assert.Equal(expected, StatusPageRequest.IsValidAccentColor(color));

    [Theory]
    [InlineData("https://acme.com/logo.svg", false, true)]
    [InlineData("http://acme.com/logo.svg", false, false)]
    [InlineData("javascript:alert(1)", true, false)]
    [InlineData("data:image/png;base64,AAAA", false, false)]
    [InlineData("https://user:pw@acme.com/x", false, false)]
    [InlineData("mailto:help@acme.com", true, true)]
    [InlineData("mailto:help@acme.com", false, false)]
    [InlineData("mailto:", true, false)]
    [InlineData("/relative.png", false, false)]
    public void IsValidLink(string url, bool allowMailto, bool expected) => Assert.Equal(expected, StatusPageRequest.IsValidLink(url, allowMailto));

    [Fact]
    public void Validate_RejectsBadBrandingFields()
    {
        var ok = new StatusPageRequest { Slug = "acme", Title = "Acme" };

        Assert.Null(ok.Validate());
        Assert.NotNull((ok with { Domain = "nope" }).Validate());
        Assert.NotNull((ok with { AccentColor = "red" }).Validate());
        Assert.NotNull((ok with { LogoUrl = "http://x.com/a.png" }).Validate());
        Assert.NotNull((ok with { SupportUrl = "ftp://x.com" }).Validate());
        Assert.Null((ok with { Domain = "Status.Acme.com", AccentColor = "#112233", LogoUrl = "https://x.com/a.png", SupportUrl = "mailto:a@x.com" }).Validate());
    }

    [Fact]
    public void Apply_NullLeavesBrandingAndEmptyClearsIt()
    {
        var page = new StatusPage
        {
            Id = Guid.NewGuid(), Slug = "acme", Title = "Acme", Domain = "status.acme.com", AccentColor = "#112233",
            LogoUrl = "https://x.com/a.png", SupportUrl = "mailto:a@x.com", CreatedAt = default, UpdatedAt = default,
        };

        var kept = StatusPageQueryService.Apply(page, new StatusPageRequest { Slug = "acme", Title = "Acme" });
        Assert.Equal("status.acme.com", kept.Domain);
        Assert.Equal("#112233", kept.AccentColor);
        Assert.Equal("https://x.com/a.png", kept.LogoUrl);
        Assert.Equal("mailto:a@x.com", kept.SupportUrl);

        var cleared = StatusPageQueryService.Apply(page, new StatusPageRequest { Slug = "acme", Title = "Acme", Domain = "", AccentColor = "", LogoUrl = "", SupportUrl = "" });
        Assert.Equal("", cleared.Domain);
        Assert.Equal("", cleared.AccentColor);
        Assert.Equal("", cleared.LogoUrl);
        Assert.Equal("", cleared.SupportUrl);
    }
}
