using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for the request/response
/// DTOs <see cref="Endpoints.MetricsEndpoints"/> serves - same camelCase/string-enum
/// conventions as <see cref="SpansJsonContext"/>. Nested types reachable from the roots
/// below (<see cref="MetricFilter"/>, <see cref="MetricAttributeFilter"/>,
/// <see cref="MetricNameInfo"/>, <see cref="MetricSeries"/>, <see cref="MetricSeriesPoint"/>,
/// <see cref="MetricPointType"/>) are picked up transitively.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(MetricNamesRequest))]
[JsonSerializable(typeof(MetricNamesResponse))]
[JsonSerializable(typeof(MetricQueryRequest))]
[JsonSerializable(typeof(MetricQueryResponse))]
public sealed partial class MetricsJsonContext : JsonSerializerContext;
