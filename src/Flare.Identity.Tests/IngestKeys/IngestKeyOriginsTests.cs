using Flare.Identity.IngestKeys;
using Xunit;

namespace Flare.Identity.Tests.IngestKeys;

public class IngestKeyOriginsTests
{
    [Fact]
    public void Normalize_LowercasesTrimsStripsTrailingSlashAndDeduplicates()
    {
        var (origins, error) = IngestKeyOrigins.Normalize([" HTTPS://App.Example.com/ ", "https://app.example.com", "", "http://localhost:5173"]);

        Assert.Null(error);
        Assert.Equal(["https://app.example.com", "http://localhost:5173"], origins);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("app.example.com")]
    [InlineData("https://app.example.com/path")]
    [InlineData("https://app.example.com?x=1")]
    [InlineData("ftp://app.example.com")]
    public void Normalize_RejectsAnythingThatIsNotABareHttpOrigin(string input)
    {
        var (origins, error) = IngestKeyOrigins.Normalize([input]);

        Assert.Null(origins);
        Assert.NotNull(error);
    }
}
