using Flare.Api.Auth;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Flare.Api.Tests.Auth;

public class CorsAllowedOriginsTests
{
    private static IConfiguration Config(params string[] origins) =>
        new ConfigurationBuilder().AddInMemoryCollection(
            origins.Select((o, i) => new KeyValuePair<string, string?>($"Cors:AllowedOrigins:{i}", o))).Build();

    [Fact]
    public void FromConfiguration_NormalizesTrailingSlashAndPath()
    {
        // The CommunityToolkit integration passes its dashboardUrl through verbatim (CommunityToolkit/Aspire#2370).
        var origins = CorsAllowedOrigins.FromConfiguration(Config("https://flare.example.com/", "https://example.com/flare")).Origins;

        Assert.Equal(["https://flare.example.com", "https://example.com"], origins);
    }

    [Fact]
    public void FromConfiguration_IsEmpty_WhenUnset()
    {
        Assert.Empty(CorsAllowedOrigins.FromConfiguration(Config()).Origins);
    }

    [Fact]
    public void FromConfiguration_RejectsWildcard_BecauseTheDefaultPolicyAllowsCredentials()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => CorsAllowedOrigins.FromConfiguration(Config("*")));

        Assert.Contains("Cors:AllowedOrigins:0", ex.Message);
    }

    [Fact]
    public void FromConfiguration_RejectsAValueThatIsNotAnAbsoluteUrl()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => CorsAllowedOrigins.FromConfiguration(Config("http://localhost:5173", "flare.example.com")));

        Assert.Contains("Cors:AllowedOrigins:1", ex.Message);
    }
}
