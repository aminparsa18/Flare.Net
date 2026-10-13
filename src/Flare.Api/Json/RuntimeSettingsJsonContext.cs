using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>Source-generated JSON contract for <see cref="Endpoints.RuntimeSettingsEndpoints"/> - camelCase, same convention as <see cref="TelemetryExportJsonContext"/>.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PiiRedactionSetting))]
[JsonSerializable(typeof(PiiRedactionSettingRequest))]
public sealed partial class RuntimeSettingsJsonContext : JsonSerializerContext;
