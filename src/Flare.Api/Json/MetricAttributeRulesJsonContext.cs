using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>Source-generated JSON contract for <see cref="Endpoints.MetricAttributeRuleEndpoints"/>, plus the <c>AttributesJson</c> round-trip <see cref="Query.MetricAttributeRuleQueryService"/> persists - camelCase, string enums, same convention as <see cref="PipelineRulesJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(MetricAttributeRuleRequest))]
[JsonSerializable(typeof(MetricAttributeRule))]
[JsonSerializable(typeof(MetricAttributeRuleListResponse))]
[JsonSerializable(typeof(IReadOnlyList<string>))]
public sealed partial class MetricAttributeRulesJsonContext : JsonSerializerContext;
