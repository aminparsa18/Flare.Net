using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>Source-generated JSON contract for <see cref="Endpoints.LogMetricEndpoints"/>, plus the <c>ConditionJson</c>/<c>GroupByJson</c> round-trips <see cref="Query.LogMetricQueryService"/> persists - camelCase, string enums, same convention as <see cref="PipelineRulesJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(LogMetricRequest))]
[JsonSerializable(typeof(LogMetric))]
[JsonSerializable(typeof(LogMetricListResponse))]
[JsonSerializable(typeof(LogMetricPreviewRequest))]
[JsonSerializable(typeof(LogMetricPreviewResponse))]
[JsonSerializable(typeof(LogFilter))]
[JsonSerializable(typeof(IReadOnlyList<string>))]
public sealed partial class LogMetricsJsonContext : JsonSerializerContext;
