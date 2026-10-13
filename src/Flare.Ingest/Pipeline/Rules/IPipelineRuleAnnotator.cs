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
        if (!options.Value.Enabled)
        {
            return Task.FromResult(events);
        }

        var piiDetectors = options.Value.Pii.EffectiveDetectors(ruleCache.PiiEnabledOverride);
        var rules = ruleCache.CurrentRules;
        if (piiDetectors == PiiDetectors.None && rules.Count == 0)
        {
            return Task.FromResult(events);
        }

        var result = new LogEvent[events.Count];
        for (var i = 0; i < events.Count; i++)
        {
            // Managed PII redaction runs before user rules so an extract rule can't copy a raw value into a new attribute.
            var logEvent = piiDetectors == PiiDetectors.None ? events[i] : RedactPii(events[i], piiDetectors);
            result[i] = rules.Count == 0 ? logEvent : PipelineRuleExecutor.Apply(logEvent, rules);
        }

        return Task.FromResult<IReadOnlyList<LogEvent>>(result);
    }

    private static LogEvent RedactPii(LogEvent logEvent, PiiDetectors detectors)
    {
        var body = logEvent.Body is null ? null : PiiRedactor.Redact(logEvent.Body, detectors);
        Dictionary<string, string>? attributes = null;
        foreach (var (key, value) in logEvent.LogAttributes)
        {
            var redacted = PiiRedactor.Redact(value, detectors);
            if (redacted != value)
            {
                attributes ??= new Dictionary<string, string>(logEvent.LogAttributes);
                attributes[key] = redacted;
            }
        }

        return body == logEvent.Body && attributes is null
            ? logEvent
            : logEvent with { Body = body, LogAttributes = attributes ?? logEvent.LogAttributes };
    }
}
