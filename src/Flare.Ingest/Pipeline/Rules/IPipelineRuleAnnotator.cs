using Flare.Ingest.Model;
using Microsoft.Extensions.Options;

namespace Flare.Ingest.Pipeline.Rules;

/// <summary>Applies every matching enabled <see cref="PipelineRule"/>'s extraction/redaction actions to a flush batch, right before the batch is written to ClickHouse.</summary>
public interface IPipelineRuleAnnotator
{
    Task<IReadOnlyList<LogEvent>> AnnotateAsync(IReadOnlyList<LogEvent> events, CancellationToken cancellationToken);
}

/// <summary>
/// See <see cref="IPipelineRuleAnnotator"/>. Deliberately called from
/// <c>ClickHouseFlushWorker.FlushAsync</c> before <c>Patterns.ILogPatternAnnotator</c> -
/// see that method's remarks for why extraction/redaction has to run first (a redacted
/// <c>Body</c> is what Drain clustering should compute its pattern template from,
/// otherwise PII could leak into a cluster template even after redaction). Synchronous
/// under the hood (<see cref="PipelineRuleExecutor"/> is pure, no I/O per event) but
/// returns a <see cref="Task"/> for shape-parity with <c>ILogPatternAnnotator.AnnotateAsync</c>,
/// which does need to await a shared-store round trip.
/// </summary>
public sealed class PipelineRuleAnnotator(IPipelineRuleCache ruleCache, IOptions<PipelineRuleOptions> options) : IPipelineRuleAnnotator
{
    public Task<IReadOnlyList<LogEvent>> AnnotateAsync(IReadOnlyList<LogEvent> events, CancellationToken cancellationToken)
    {
        var rules = ruleCache.CurrentRules;
        if (!options.Value.Enabled || rules.Count == 0)
        {
            return Task.FromResult(events);
        }

        var result = new LogEvent[events.Count];
        for (var i = 0; i < events.Count; i++)
        {
            result[i] = PipelineRuleExecutor.Apply(events[i], rules);
        }

        return Task.FromResult<IReadOnlyList<LogEvent>>(result);
    }
}
