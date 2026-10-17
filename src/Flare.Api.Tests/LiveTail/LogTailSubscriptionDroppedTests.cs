using Flare.Api.LiveTail;
using Flare.Api.Model;
using Xunit;

namespace Flare.Api.Tests.LiveTail;

public class LogTailSubscriptionDroppedTests
{
    private static LogEventDto Event(string body) => new()
    {
        EventId = Guid.NewGuid(),
        Timestamp = DateTimeOffset.UnixEpoch,
        ObservedTimestamp = DateTimeOffset.UnixEpoch,
        IngestedAt = DateTimeOffset.UnixEpoch,
        TraceId = string.Empty,
        SpanId = string.Empty,
        TraceFlags = 0,
        SeverityText = string.Empty,
        SeverityNumber = 9,
        ServiceName = "svc",
        Body = body,
        ResourceSchemaUrl = string.Empty,
        ResourceAttributes = new Dictionary<string, string>(),
        ScopeSchemaUrl = string.Empty,
        ScopeName = string.Empty,
        ScopeVersion = string.Empty,
        ScopeAttributes = new Dictionary<string, string>(),
        LogAttributes = new Dictionary<string, string>(),
        EventName = string.Empty,
        PatternId = string.Empty,
        PatternTemplate = string.Empty,
    };

    [Fact]
    public void DroppedEvents_AreKeptOnlyWhenAskedFor()
    {
        var subscription = new LogTailSubscription(1, "u");

        subscription.TryPublishEvent(Event("fits"));
        subscription.TryPublishEvent(Event("lost"));

        Assert.Equal(1, subscription.TakeDroppedCount());
        Assert.Empty(subscription.TakeDroppedEvents());
    }

    [Fact]
    public void DroppedEvents_AreSampledUpToTheLimitAndTakenOnce()
    {
        var subscription = new LogTailSubscription(1, "u") { DroppedSampleLimit = 2 };

        subscription.TryPublishEvent(Event("fits"));
        foreach (var body in new[] { "lost 1", "lost 2", "lost 3" })
        {
            subscription.TryPublishEvent(Event(body));
        }

        // All three losses are counted, the first two are remembered, oldest first.
        Assert.Equal(3, subscription.TakeDroppedCount());
        Assert.Equal(["lost 1", "lost 2"], subscription.TakeDroppedEvents().Select(e => e.Body));
        Assert.Empty(subscription.TakeDroppedEvents());
    }
}
