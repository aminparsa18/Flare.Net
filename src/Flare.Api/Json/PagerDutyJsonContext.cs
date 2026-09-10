using System.Text.Json.Serialization;
using Flare.Api.Alerting;

namespace Flare.Api.Json;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for parsing PagerDuty's
/// Events API v2 <c>enqueue</c> response in <see cref="PagerDutyAlertNotifier"/> - same
/// camelCase-on-the-wire convention as <see cref="AlertsJsonContext"/>/<see cref="TelegramJsonContext"/>,
/// kept in its own context since <see cref="PagerDutyEnqueueResponse"/> is an inbound
/// shape from a third party, not one of this API's own DTOs.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PagerDutyEnqueueResponse))]
public sealed partial class PagerDutyJsonContext : JsonSerializerContext;
