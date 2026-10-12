using Flare.Api.Endpoints;
using Flare.Api.Model;
using Xunit;

namespace Flare.Api.Tests.Endpoints;

public class IngestApiKeyEndpointsTests
{
    [Fact]
    public void ParseExpiry_NullMeansNever_FutureIsAccepted_PastAndOutOfRangeAreRejected()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Equal((null, null), IngestApiKeyEndpoints.ParseExpiry(null, now));

        var future = now.AddDays(1);
        var ok = IngestApiKeyEndpoints.ParseExpiry(future.ToUnixTimeMilliseconds(), now);
        Assert.Null(ok.Error);
        Assert.Equal(future.ToUnixTimeMilliseconds(), ok.ExpiresAt!.Value.ToUnixTimeMilliseconds());

        Assert.NotNull(IngestApiKeyEndpoints.ParseExpiry(now.AddMinutes(-1).ToUnixTimeMilliseconds(), now).Error);
        Assert.NotNull(IngestApiKeyEndpoints.ParseExpiry(-5, now).Error);
        Assert.NotNull(IngestApiKeyEndpoints.ParseExpiry(long.MaxValue, now).Error);
    }

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
