using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>Source-generated JSON contract for <see cref="Endpoints.IssueTrackerEndpoints"/> - camelCase, string enums, same convention as <see cref="SourceLinksJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(IssueTrackerDto))]
[JsonSerializable(typeof(IssueTrackerListResponse))]
public sealed partial class IssueTrackersJsonContext : JsonSerializerContext;
