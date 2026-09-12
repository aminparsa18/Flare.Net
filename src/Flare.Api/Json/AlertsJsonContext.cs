using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for the alert-rule DTOs
/// <see cref="Endpoints.AlertEndpoints"/> serves - camelCase, string enums, same
/// convention as <see cref="LogsJsonContext"/> - and also for the internal
/// <c>ConditionJson</c>/<c>MetricConditionJson</c>/<c>ExceptionConditionJson</c> round-trips
/// <see cref="Query.AlertQueryService"/> uses to persist/read back an <see cref="AlertRule"/>'s
/// <see cref="LogFilter"/>/<see cref="MetricAlertCondition"/>/<see cref="ExceptionCountCondition"/>
/// condition.
/// </summary>
/// <remarks>
/// <see cref="LogFilter"/>/<see cref="MetricAlertCondition"/>/<see cref="ExceptionCountCondition"/>
/// are listed explicitly rather than left to transitive discovery: unlike
/// <see cref="LogsJsonContext"/> (where <see cref="LogFilter"/> is only ever reached as a
/// nested property), <see cref="Query.AlertQueryService"/> serializes/deserializes all three
/// standalone.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AlertRuleRequest))]
[JsonSerializable(typeof(AlertRule))]
[JsonSerializable(typeof(AlertRuleListResponse))]
[JsonSerializable(typeof(AlertHistoryResponse))]
[JsonSerializable(typeof(AlertTestResult))]
[JsonSerializable(typeof(AlertNotificationTestResult))]
[JsonSerializable(typeof(LogFilter))]
[JsonSerializable(typeof(MetricAlertCondition))]
[JsonSerializable(typeof(ExceptionCountCondition))]
[JsonSerializable(typeof(IReadOnlyList<AlertChannelResult>))]
public sealed partial class AlertsJsonContext : JsonSerializerContext;
