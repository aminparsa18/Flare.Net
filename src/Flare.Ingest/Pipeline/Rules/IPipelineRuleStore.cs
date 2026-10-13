namespace Flare.Ingest.Pipeline.Rules;

/// <summary>Read-only access to enabled <see cref="PipelineRule"/>s. Abstracted behind an interface so <see cref="PipelineRuleCache"/>'s poll loop is unit-testable against a fake, same reasoning <see cref="IClickHouseLogEventWriter"/> is abstracted on the write side.</summary>
public interface IPipelineRuleStore
{
    Task<IReadOnlyList<PipelineRule>> GetEnabledRulesAsync(CancellationToken cancellationToken);

    /// <summary>The dashboard's managed-PII-redaction override from <c>runtime_settings</c> (ADR-0210); <see langword="null"/> when none is saved, i.e. follow <see cref="PipelineRuleOptions.Pii"/>.</summary>
    Task<bool?> GetPiiEnabledOverrideAsync(CancellationToken cancellationToken);
}
