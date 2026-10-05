using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>Source-generated <see cref="System.Text.Json"/> contract for <see cref="Endpoints.ErrorIssueEndpoints"/> - same camelCase/string-enum convention as <see cref="MaintenanceWindowsJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ErrorIssueRequest))]
[JsonSerializable(typeof(ErrorIssue))]
[JsonSerializable(typeof(ErrorIssueListResponse))]
public sealed partial class ErrorIssuesJsonContext : JsonSerializerContext;
