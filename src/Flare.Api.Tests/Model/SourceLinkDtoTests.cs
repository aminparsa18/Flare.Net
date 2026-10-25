using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Identity.SourceLinks;
using Xunit;

namespace Flare.Api.Tests.Model;

public class SourceLinkDtoTests
{
    private static SourceLinkDto Dto(string repoUrl = "https://github.com/acme/shop") => new()
    {
        ServiceName = "checkout-api",
        Provider = SourceLinkProvider.GitHub,
        RepoUrl = repoUrl,
        DefaultRef = "main",
        PathPrefix = "/src/"
    };

    [Fact]
    public void Validate_WithAFullConfig_ReturnsNull() => Assert.Null(Dto().Validate());

    [Theory]
    [InlineData("acme/shop")]
    [InlineData("javascript:alert(1)")]
    public void Validate_WithANonHttpRepoUrl_ReturnsAnError(string repoUrl) => Assert.NotNull(Dto(repoUrl).Validate());

    [Fact]
    public void Validate_WithOversizedFields_ReturnsAnError()
    {
        Assert.NotNull((Dto() with { DefaultRef = new string('a', 201) }).Validate());
        Assert.NotNull((Dto() with { PathPrefix = new string('a', 501) }).Validate());
    }

    // System.Text.Json assigns every mapped init property, so a field the client omits arrives as null
    // and overwrites the "" initializer.
    [Fact]
    public void Validate_WithOmittedOptionalStrings_ReturnsNull() =>
        Assert.Null((Dto() with { DefaultRef = null!, PathPrefix = null! }).Validate());

    [Fact]
    public void Deserialize_MinimalBody_Validates()
    {
        const string json = """{"serviceName":"svc","provider":"GitHub","repoUrl":"https://github.com/acme/shop"}""";

        var dto = JsonSerializer.Deserialize(json, SourceLinksJsonContext.Default.SourceLinkDto);

        Assert.NotNull(dto);
        Assert.Null(dto.Validate());
        Assert.Null(dto.AccessToken);
        Assert.Null(dto.IssueToken);
    }
}
