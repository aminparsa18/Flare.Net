using System.Text.Json;
using System.Text.Json.Serialization;

namespace Flare.Mcp;

internal sealed class MetricNamesRequestWire
{
    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    public IReadOnlyList<string>? Services { get; init; }
}

internal sealed class MetricNameInfoWire
{
    public required string MetricName { get; init; }

    public required string ServiceName { get; init; }

    /// <summary>"Gauge" | "Sum" | "Histogram" | "ExponentialHistogram" - see MetricPointType.</summary>
    public required string Type { get; init; }

    public string? Unit { get; init; }

    public string? Description { get; init; }

    public long SeriesCount { get; init; }
}

internal sealed class MetricNamesResponseWire
{
    public List<MetricNameInfoWire> Metrics { get; init; } = [];
}

internal sealed class MetricFilterWire
{
    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    public IReadOnlyList<string>? Services { get; init; }
}

internal sealed class MetricQueryRequestWire
{
    public required string MetricName { get; init; }

    public required string Type { get; init; }

    public MetricFilterWire? Filter { get; init; }

    public int BucketWidthSeconds { get; init; }

    public string? GroupByAttributeKey { get; init; }
}

internal sealed class MetricSeriesPointWire
{
    public DateTimeOffset BucketStart { get; init; }

    /// <summary>Gauge: average value in the bucket. Sum: max(Value)-min(Value) in the bucket.</summary>
    public double? Value { get; init; }

    /// <summary>Sum: raw sample row count. Histogram: total observation count.</summary>
    public long? Count { get; init; }

    /// <summary>Histogram only: total of observed values in the bucket.</summary>
    public double? Sum { get; init; }

    public double? P50 { get; init; }

    public double? P75 { get; init; }

    public double? P90 { get; init; }

    public double? P95 { get; init; }

    public double? P99 { get; init; }

    /// <summary>Histogram only, approximate - see MetricSeriesPoint.MaxApprox's C# remarks.</summary>
    public double? MaxApprox { get; init; }
}

internal sealed class MetricSeriesWire
{
    public required string ServiceName { get; init; }

    public Dictionary<string, string> Attributes { get; init; } = [];

    public List<MetricSeriesPointWire> Points { get; init; } = [];
}

internal sealed class MetricQueryResponseWire
{
    public List<MetricSeriesWire> Series { get; init; } = [];

    /// <summary>A Gauge the admin marked "treat as counter" (ADR-0066) - its points are Sum-shaped.</summary>
    public bool TreatedAsCounter { get; init; }
}

internal static class MetricsWireJsonOptions
{
    public static readonly JsonSerializerOptions Instance = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}
