using Flare.Ingest.Otlp;
using Xunit;

namespace Flare.Ingest.Tests.Otlp;

public class OtlpBrowserOriginsTests
{
    [Theory]
    [InlineData("https://app.example.com", true)]
    [InlineData("https://APP.example.com", true)]
    [InlineData("http://localhost:5173", true)]
    [InlineData("https://evil.example", false)]
    public void IsConfigured_MatchesTheBrowserOrigin_WhenConfiguredWithATrailingSlashOrPath(string origin, bool expected)
    {
        var origins = OtlpBrowserOrigins.FromOptions(new OtlpReceiverOptions
        {
            AllowedOrigins = ["https://app.example.com/", "http://localhost:5173/shop", ""],
        });

        Assert.Equal(expected, origins.IsConfigured(origin));
    }

    [Fact]
    public void Wildcard_AllowsAnyOrigin()
    {
        var origins = OtlpBrowserOrigins.FromOptions(new OtlpReceiverOptions { AllowedOrigins = ["*"] });

        Assert.True(origins.IsConfigured("https://anything.example"));
    }

    [Fact]
    public void InvalidEntry_FailsStartup_NamingTheKey()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            OtlpBrowserOrigins.FromOptions(new OtlpReceiverOptions { AllowedOrigins = ["app.example.com"] }));

        Assert.Contains("Otlp:AllowedOrigins:0", ex.Message);
    }
}
