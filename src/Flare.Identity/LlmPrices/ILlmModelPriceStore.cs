namespace Flare.Identity.LlmPrices;

/// <summary>An admin's price for one model, in USD per million tokens.</summary>
public sealed record LlmModelPrice(string Model, double InputPerMillion, double OutputPerMillion);

/// <summary>
/// Admin overrides of the built-in model prices behind the <c>/llm</c> page's estimated cost -
/// see docs-internal/adr/0101-llm-estimated-cost.md. Only overrides are stored; a model with no
/// row falls back to the built-in default (or to no price).
/// </summary>
public interface ILlmModelPriceStore
{
    /// <summary>Every override keyed by model name, case-insensitively. The whole map, like <see cref="MetricMetadata.IMetricMetadataOverrideStore.GetAllAsync"/> - one row per model an admin priced.</summary>
    Task<IReadOnlyDictionary<string, LlmModelPrice>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Upserts the price for one model.</summary>
    Task SetAsync(LlmModelPrice price, CancellationToken cancellationToken = default);

    /// <summary>Removes a model's override. A no-op if none exists.</summary>
    Task ResetAsync(string model, CancellationToken cancellationToken = default);
}
