using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for the pipeline-rule DTOs
/// <see cref="Endpoints.PipelineRuleEndpoints"/> serves - camelCase, string enums, same
/// convention as <see cref="AlertsJsonContext"/> - and also for the internal
/// <c>ConditionJson</c>/<c>ActionsJson</c> round-trips <see cref="Query.PipelineRuleQueryService"/>
/// uses to persist/read back a <see cref="PipelineRule"/>'s <see cref="LogFilter"/> condition
/// and <see cref="PipelineRuleAction"/> list.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(PipelineRuleRequest))]
[JsonSerializable(typeof(PipelineRule))]
[JsonSerializable(typeof(PipelineRuleListResponse))]
[JsonSerializable(typeof(LogFilter))]
[JsonSerializable(typeof(IReadOnlyList<PipelineRuleAction>))]
public sealed partial class PipelineRulesJsonContext : JsonSerializerContext;
