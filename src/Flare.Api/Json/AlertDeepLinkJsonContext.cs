using System.Text.Json.Serialization;
using Flare.Api.Alerting;

namespace Flare.Api.Json;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for the Logs Explorer state
/// <see cref="AlertMessageFormatter.BuildMatchingLogsUrl"/> embeds in a fired alert's
/// deep link - camelCase + string enums to match what the dashboard's own
/// <c>AttributeFilter</c>/<c>BodyJsonFilter</c> types expect, and nulls omitted so an
/// unset <c>AttributeFilter.Values</c> reads as absent rather than <c>null</c>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LogsDeepLinkState))]
internal sealed partial class AlertDeepLinkJsonContext : JsonSerializerContext;
