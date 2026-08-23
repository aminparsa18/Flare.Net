using Flare.Ingest.Model;
using Microsoft.Extensions.Options;

namespace Flare.Ingest.Patterns;

/// <summary>Annotates a flush batch with <see cref="LogEvent.PatternId"/>/<see cref="LogEvent.PatternTemplate"/> via <see cref="ILogPatternMatcher"/>, right before the batch is written to ClickHouse.</summary>
public interface ILogPatternAnnotator
{
    Task<IReadOnlyList<LogEvent>> AnnotateAsync(IReadOnlyList<LogEvent> events, CancellationToken cancellationToken);
}

/// <summary>
/// See <see cref="ILogPatternAnnotator"/>. Deliberately called from
/// <c>ClickHouseFlushWorker.FlushAsync</c> (the batched, background-service side of the
/// pipeline), not from <c>OtlpLogMapper</c>/the OTLP receive endpoints - keeps the
/// CPU-bound Drain matching off the client's export request path, matching Planning.md's
/// design decision for this feature. Async because <see cref="ILogPatternMatcher.MatchBatchAsync"/>
/// may involve a shared-store round trip (see <see cref="DrainPatternMatcher"/>'s remarks) -
/// still a single call for the whole batch, not one await per event.
/// </summary>
public sealed class LogPatternAnnotator(ILogPatternMatcher matcher, IOptions<LogPatternOptions> options) : ILogPatternAnnotator
{
    public async Task<IReadOnlyList<LogEvent>> AnnotateAsync(IReadOnlyList<LogEvent> events, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return events;
        }

        var bodies = new string?[events.Count];
        for (var i = 0; i < events.Count; i++)
        {
            bodies[i] = events[i].Body;
        }

        var matches = await matcher.MatchBatchAsync(bodies, cancellationToken);

        var result = new LogEvent[events.Count];
        for (var i = 0; i < events.Count; i++)
        {
            result[i] = events[i] with { PatternId = matches[i].PatternId, PatternTemplate = matches[i].PatternTemplate };
        }
        return result;
    }
}
