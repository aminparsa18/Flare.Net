using System.Text.Json.Serialization;
using Flare.Api.Alerting;

namespace Flare.Api.Json;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for the page states
/// <see cref="AlertMessageFormatter.BuildFiredDataUrl"/> embeds in a fired alert's
/// deep link (Logs, Metrics, Exceptions) - camelCase + string enums to match what the
/// dashboard's own <c>AttributeFilter</c>/<c>BodyJsonFilter</c>/<c>MetricPointType</c> types
/// expect, and nulls omitted so an unset <c>AttributeFilter.Values</c> reads as absent rather than <c>null</c>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LogsDeepLinkState))]
[JsonSerializable(typeof(MetricsDeepLinkState))]
[JsonSerializable(typeof(ErrorsDeepLinkState))]
internal sealed partial class AlertDeepLinkJsonContext : JsonSerializerContext;
