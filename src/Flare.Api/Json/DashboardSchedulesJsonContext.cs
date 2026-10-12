using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for the dashboard schedule DTOs
/// <see cref="Endpoints.DashboardScheduleEndpoints"/> serves - same convention as <see cref="OnCallRotationsJsonContext"/>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(DashboardScheduleRequest))]
[JsonSerializable(typeof(DashboardSchedule))]
[JsonSerializable(typeof(DashboardScheduleListResponse))]
[JsonSerializable(typeof(DashboardReportRunListResponse))]
public sealed partial class DashboardSchedulesJsonContext : JsonSerializerContext;
