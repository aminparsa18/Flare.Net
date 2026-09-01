using Flare.Ingest.Model;
using Flare.Ingest.Patterns;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flare.Ingest.Tests.Patterns;

public class LogPatternAnnotatorTests
{
    [Fact]
    public async Task AnnotateAsync_SetsPatternIdAndTemplate_FromMatcherResult()
    {
        var matcher = new FakeLogPatternMatcher { Result = new PatternMatch("p1", "GET /api/orders/<*>") };
        var annotator = new LogPatternAnnotator(matcher, Options.Create(new LogPatternOptions { Enabled = true }));
        var events = new[] { MinimalLogEvent() with { Body = "GET /api/orders/123" } };

        var result = await annotator.AnnotateAsync(events, CancellationToken.None);

        Assert.Equal("p1", result[0].PatternId);
        Assert.Equal("GET /api/orders/<*>", result[0].PatternTemplate);
        Assert.Equal(["GET /api/orders/123"], matcher.MatchedBodies);
    }

    [Fact]
    public async Task AnnotateAsync_PreservesBatchOrder()
    {
        var matcher = new FakeLogPatternMatcher();
        var annotator = new LogPatternAnnotator(matcher, Options.Create(new LogPatternOptions { Enabled = true }));
        var events = new[]
        {
            MinimalLogEvent() with { Body = "first" },
            MinimalLogEvent() with { Body = "second" },
        };

        var result = await annotator.AnnotateAsync(events, CancellationToken.None);

        Assert.Equal("first", result[0].Body);
        Assert.Equal("second", result[1].Body);
    }

    [Fact]
    public async Task AnnotateAsync_IsANoOp_WhenDisabled()
    {
        var matcher = new FakeLogPatternMatcher();
        var annotator = new LogPatternAnnotator(matcher, Options.Create(new LogPatternOptions { Enabled = false }));
        var events = new[] { MinimalLogEvent() };

        var result = await annotator.AnnotateAsync(events, CancellationToken.None);

        Assert.Same(events, result);
        Assert.Empty(matcher.MatchedBodies);
    }

    private static LogEvent MinimalLogEvent() => new()
    {
        EventId = Guid.NewGuid(),
        Timestamp = DateTimeOffset.UnixEpoch,
        IngestedAt = DateTimeOffset.UnixEpoch,
        SeverityNumber = 9,
        ResourceAttributes = new Dictionary<string, string>(),
        ScopeAttributes = new Dictionary<string, string>(),
        LogAttributes = new Dictionary<string, string>(),
    };
}
