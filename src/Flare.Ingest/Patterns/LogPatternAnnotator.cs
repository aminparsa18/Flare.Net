using Flare.Ingest.Model;
using Microsoft.Extensions.Options;

namespace Flare.Ingest.Patterns;

/// <summary>Annotates a flush batch with <see cref="LogEvent.PatternId"/>/<see cref="LogEvent.PatternTemplate"/> via <see cref="ILogPatternMatcher"/>, right before the batch is written to ClickHouse.</summary>
public interface ILogPatternAnnotator
{
    IReadOnlyList<LogEvent> Annotate(IReadOnlyList<LogEvent> events);
}

/// <summary>
/// See <see cref="ILogPatternAnnotator"/>. Deliberately called from
/// <c>ClickHouseFlushWorker.FlushAsync</c> (the batched, background-service side of the
/// pipeline), not from <c>OtlpLogMapper</c>/the OTLP receive endpoints - keeps the
/// CPU-bound Drain matching off the client's export request path, matching Planning.md's
/// design decision for this feature.
/// </summary>
public sealed class LogPatternAnnotator(ILogPatternMatcher matcher, IOptions<LogPatternOptions> options) : ILogPatternAnnotator
{
    public IReadOnlyList<LogEvent> Annotate(IReadOnlyList<LogEvent> events)
    {
        if (!options.Value.Enabled)
        {
            return events;
        }

        var result = new LogEvent[events.Count];
        for (var i = 0; i < events.Count; i++)
        {
            var match = matcher.Match(events[i].Body);
            result[i] = events[i] with { PatternId = match.PatternId, PatternTemplate = match.PatternTemplate };
        }
        return result;
    }
}
