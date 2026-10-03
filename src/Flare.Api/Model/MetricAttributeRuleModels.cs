using MemoryPack;

namespace Flare.Api.Model;

/// <summary>What a <see cref="MetricAttributeRule"/> does to the attributes it lists.</summary>
public enum MetricAttributeRuleMode
{
    /// <summary>Remove the listed data-point attributes and keep the rest.</summary>
    Drop,

    /// <summary>Keep only the listed data-point attributes and remove the rest.</summary>
    KeepOnly,
}

/// <summary>
/// A saved per-metric attribute reduction rule, applied by <c>Flare.Ingest</c>'s
/// <c>MetricAttributeReducer</c> at flush time. Strips high-cardinality data-point
/// attributes (<c>http.url</c>, <c>user.id</c>, a request ID) before they multiply a
/// metric's series count in ClickHouse. See docs-internal/adr/0083-metric-attribute-reduction.md.
/// </summary>
[MemoryPackable]
public sealed partial record MetricAttributeRule
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = "";

    public bool Enabled { get; init; } = true;

    /// <summary>Exact metric name, or a prefix followed by a single trailing <c>*</c> (<c>http.client.*</c>).</summary>
    public required string MetricName { get; init; }

    public required MetricAttributeRuleMode Mode { get; init; }

    /// <summary>Data-point attribute keys the <see cref="Mode"/> applies to.</summary>
    public required IReadOnlyList<string> Attributes { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

[MemoryPackable]
public sealed partial record MetricAttributeRuleListResponse
{
    public required IReadOnlyList<MetricAttributeRule> Rules { get; init; }
}

/// <summary>Create/update request body for <c>/api/metric-attribute-rules</c>. <see cref="Enabled"/> is nullable for the same omitted-vs-false reason as <see cref="PipelineRuleRequest"/>.</summary>
[MemoryPackable]
public sealed partial record MetricAttributeRuleRequest
{
    public required string Name { get; init; }

    public string? Description { get; init; }

    public bool? Enabled { get; init; }

    public required string MetricName { get; init; }

    public required MetricAttributeRuleMode Mode { get; init; }

    public IReadOnlyList<string>? Attributes { get; init; }

    /// <summary>
    /// A non-blank name/metric name, a <c>*</c> only as the metric name's last character
    /// (and not alone - that would reduce every metric), and at least one non-blank
    /// attribute key. A <see cref="MetricAttributeRuleMode.KeepOnly"/> rule with no keys
    /// would strip every attribute, so it needs a key too. Returns an error message, or
    /// null when valid.
    /// </summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            return "Name is required.";
        }

        if (string.IsNullOrWhiteSpace(MetricName))
        {
            return "Metric name is required.";
        }

        var star = MetricName.IndexOf('*');
        if (star >= 0 && (star != MetricName.Length - 1 || MetricName.Length == 1))
        {
            return "Metric name may only use a single trailing '*' after a non-empty prefix, e.g. http.client.*";
        }

        if (Attributes is not { Count: > 0 } || Attributes.Any(string.IsNullOrWhiteSpace))
        {
            return "At least one non-blank attribute key is required.";
        }

        return null;
    }
}
