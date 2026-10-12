using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>Source-generated JSON contract for the telemetry export endpoints, the <c>ConfigJson</c> column round-trip, and the status blobs Flare.Ingest and Flare.AlertWorker keep in Redis - camelCase, string enums.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ForwardingTarget))]
[JsonSerializable(typeof(ForwardingTargetRequest))]
[JsonSerializable(typeof(ForwardingTargetListResponse))]
[JsonSerializable(typeof(ForwardingStatusResponse))]
[JsonSerializable(typeof(ArchiveSettings))]
[JsonSerializable(typeof(ArchiveSettingsRequest))]
[JsonSerializable(typeof(ArchiveStatusResponse))]
[JsonSerializable(typeof(ArchiveTableStatus))]
[JsonSerializable(typeof(Query.ForwardingConfig))]
[JsonSerializable(typeof(Query.ArchiveConfig))]
public sealed partial class TelemetryExportJsonContext : JsonSerializerContext;
