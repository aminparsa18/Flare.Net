using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for the notification-channel
/// DTOs <see cref="Endpoints.NotificationChannelEndpoints"/> serves - same
/// camelCase/string-enum convention as <see cref="AlertsJsonContext"/>, kept as its own
/// context rather than added to that one so each endpoint file's JSON contract stays
/// scoped to the types it actually serializes (same precedent
/// <see cref="AlertsJsonContext"/> itself set alongside <see cref="LogsJsonContext"/>).
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(NotificationChannelRequest))]
[JsonSerializable(typeof(NotificationChannel))]
[JsonSerializable(typeof(NotificationChannelListResponse))]
public sealed partial class NotificationChannelsJsonContext : JsonSerializerContext;
