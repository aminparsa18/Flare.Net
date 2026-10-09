using Flare.Ingest.Forwarding;
using Xunit;

namespace Flare.Ingest.Tests.Forwarding;

public class ManagedForwardingTests
{
    [Fact]
    public void FromRow_MapsApiWrittenConfigJson()
    {
        var keyId = Guid.NewGuid();
        var id = Guid.NewGuid();
        var json = $$"""
            {"endpoint":"http://collector:4318","headers":{"Authorization":"Bearer x"},"signals":["Logs","Metrics"],
             "services":["svc-a"],"ingestKeyIds":["{{keyId}}"],"gzip":false}
            """;

        var target = ManagedForwardingTarget.FromRow(id, "siem", json);

        Assert.NotNull(target);
        Assert.Equal(id, target.Id);
        Assert.Equal("siem", target.Options.Name);
        Assert.Equal("http://collector:4318", target.Options.Endpoint);
        Assert.Equal("Bearer x", target.Options.Headers["Authorization"]);
        Assert.Equal([ForwardingSignal.Logs, ForwardingSignal.Metrics], target.Options.Signals);
        Assert.Equal(["svc-a"], target.Options.Services);
        Assert.Equal([keyId], target.Options.IngestKeyIds);
        Assert.False(target.Options.Gzip);
    }

    [Fact]
    public void FromRow_AppliesDefaultsForMissingFields()
    {
        var target = ManagedForwardingTarget.FromRow(Guid.NewGuid(), "t", """{"endpoint":"http://x"}""");

        Assert.NotNull(target);
        Assert.True(target.Options.Gzip);
        Assert.Empty(target.Options.Signals);
        Assert.Empty(target.Options.Services);
    }

    [Fact]
    public void FromRow_ReturnsNullForJsonNull() =>
        Assert.Null(ManagedForwardingTarget.FromRow(Guid.NewGuid(), "t", "null"));

    [Theory]
    [InlineData(1_000_000, 1_000_000 + 6 * 3600 * 1000 + 1, true)]
    [InlineData(1_000_000, 1_000_000 + 6 * 3600 * 1000, false)]
    [InlineData(1_000_000, 1_000_000 + 1000, false)]
    public void IsExpired_ComparesStreamIdTimestampWithMaxAge(long enqueuedMs, long nowMs, bool expected)
    {
        var now = DateTimeOffset.FromUnixTimeMilliseconds(nowMs);

        Assert.Equal(expected, OtlpForwarder.IsExpired($"{enqueuedMs}-0", now, TimeSpan.FromHours(6)));
    }

    [Fact]
    public void IsExpired_TreatsUnparseableIdAsFresh() =>
        Assert.False(OtlpForwarder.IsExpired("garbage", DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1)));
}
