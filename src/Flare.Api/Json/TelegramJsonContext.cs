using System.Text.Json.Serialization;
using Flare.Api.Alerting;

namespace Flare.Api.Json;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for parsing Telegram's
/// <c>sendMessage</c> response in <see cref="TelegramAlertNotifier"/> - same
/// camelCase-on-the-wire convention as <see cref="AlertsJsonContext"/>/<see cref="LogsJsonContext"/>,
/// kept in its own context since <see cref="TelegramSendMessageResponse"/> is an inbound
/// shape from a third party, not one of this API's own DTOs.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TelegramSendMessageResponse))]
public sealed partial class TelegramJsonContext : JsonSerializerContext;
