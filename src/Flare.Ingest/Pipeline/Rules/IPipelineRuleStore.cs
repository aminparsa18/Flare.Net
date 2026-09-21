namespace Flare.Ingest.Pipeline.Rules;

/// <summary>Read-only access to enabled <see cref="PipelineRule"/>s. Abstracted behind an interface so <see cref="PipelineRuleCache"/>'s poll loop is unit-testable against a fake, same reasoning <see cref="IClickHouseLogEventWriter"/> is abstracted on the write side.</summary>
public interface IPipelineRuleStore
{
    Task<IReadOnlyList<PipelineRule>> GetEnabledRulesAsync(CancellationToken cancellationToken);
}
