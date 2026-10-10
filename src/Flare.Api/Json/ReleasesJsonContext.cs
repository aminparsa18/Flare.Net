using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>Source-generated <see cref="System.Text.Json"/> contract for <see cref="Endpoints.ReleaseEndpoints"/> - same convention as <see cref="ErrorIssuesJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ReleaseRequest))]
[JsonSerializable(typeof(Release))]
[JsonSerializable(typeof(ReleaseListResponse))]
[JsonSerializable(typeof(ReleaseErrorsResponse))]
public sealed partial class ReleasesJsonContext : JsonSerializerContext;
