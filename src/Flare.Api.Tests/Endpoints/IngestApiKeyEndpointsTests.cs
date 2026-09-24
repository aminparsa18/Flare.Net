using Flare.Api.Endpoints;
using Flare.Api.Model;
using Xunit;

namespace Flare.Api.Tests.Endpoints;

public class IngestApiKeyEndpointsTests
{
    [Fact]
    public void ValidateLimits_AcceptsAllCapsUnset()
    {
        Assert.Null(IngestApiKeyEndpoints.ValidateLimits(new UpdateIngestApiKeyLimitsRequest { LimitsEnabled = true }));
    }

    [Fact]
    public void ValidateLimits_AcceptsPositiveCaps()
    {
        var request = new UpdateIngestApiKeyLimitsRequest
        {
            LimitsEnabled = true,
            MaxEventsPerMinute = 1,
            MaxBytesPerMinute = 1_000,
            MaxEventsPerDay = 1_000_000,
            MaxBytesPerDay = 10_000_000_000,
        };

        Assert.Null(IngestApiKeyEndpoints.ValidateLimits(request));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ValidateLimits_RejectsANonPositiveCap_NamingIt(long value)
    {
        var error = IngestApiKeyEndpoints.ValidateLimits(new UpdateIngestApiKeyLimitsRequest { LimitsEnabled = true, MaxBytesPerDay = value });

        Assert.NotNull(error);
        Assert.Contains(nameof(UpdateIngestApiKeyLimitsRequest.MaxBytesPerDay), error);
    }
}
