using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>Source-generated JSON contract for <see cref="Endpoints.ProjectEndpoints"/> - camelCase, string enums.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ProjectDto))]
[JsonSerializable(typeof(ProjectListResponse))]
[JsonSerializable(typeof(ProjectMemberListResponse))]
[JsonSerializable(typeof(SetProjectMemberRequest))]
public sealed partial class ProjectsJsonContext : JsonSerializerContext;
