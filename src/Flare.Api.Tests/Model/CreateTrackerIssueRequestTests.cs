using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Xunit;

namespace Flare.Api.Tests.Model;

public class CreateTrackerIssueRequestTests
{
    private static CreateTrackerIssueRequest Request() => new()
    {
        Target = IssueTrackerTarget.GitHub, ServiceName = "shop", ExceptionType = "System.InvalidOperationException", Title = "T", Body = "B"
    };

    [Fact]
    public void Validate_WithAFullRequest_ReturnsNull() => Assert.Null(Request().Validate());

    [Fact]
    public void Validate_WithAnOversizedBody_ReturnsAnError() =>
        Assert.NotNull((Request() with { Body = new string('a', CreateTrackerIssueRequest.MaxBodyLength + 1) }).Validate());

    // System.Text.Json assigns every mapped init property, so a field the client omits arrives as null
    // and overwrites the "" initializer.
    [Fact]
    public void Validate_WithAnOmittedBody_ReturnsNull() => Assert.Null((Request() with { Body = null! }).Validate());

    [Fact]
    public void Deserialize_MinimalBody_Validates()
    {
        const string json = """{"target":"GitHub","serviceName":"shop","exceptionType":"System.InvalidOperationException","title":"T"}""";

        var request = JsonSerializer.Deserialize(json, ErrorIssuesJsonContext.Default.CreateTrackerIssueRequest);

        Assert.NotNull(request);
        Assert.Null(request.Validate());
    }
}
