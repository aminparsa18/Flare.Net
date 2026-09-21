namespace Flare.Ingest.Pipeline.Rules;

/// <summary>In-memory snapshot of the currently enabled <see cref="PipelineRule"/>s, kept fresh by <see cref="PipelineRuleCache"/>'s poll loop. Abstracted behind an interface so <see cref="PipelineRuleAnnotator"/> is unit-testable against a fake snapshot.</summary>
public interface IPipelineRuleCache
{
    IReadOnlyList<PipelineRule> CurrentRules { get; }
}
