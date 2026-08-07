using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for the live-tail WebSocket
/// protocol's client/server message envelopes - camelCase, enums as strings, same
/// conventions as <see cref="LogsJsonContext"/> (this is a public-facing wire format too,
/// just over a WebSocket instead of request/response HTTP bodies). Kept as its own context
/// rather than folded into <see cref="LogsJsonContext"/> since these are a distinct
/// message-envelope protocol, not request/response DTOs for
/// <see cref="Endpoints.LogsEndpoints"/>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(LogTailClientMessage))]
[JsonSerializable(typeof(LogTailServerMessage))]
public sealed partial class LogTailJsonContext : JsonSerializerContext;
