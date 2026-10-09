using System.Text.Json.Serialization;

namespace Flare.Api.Export;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ArchiveWorkerState))]
public sealed partial class ExportStatusJsonContext : JsonSerializerContext;
