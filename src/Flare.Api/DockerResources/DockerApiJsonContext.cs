using System.Text.Json.Serialization;

namespace Flare.Api.DockerResources;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for parsing the Docker Engine
/// API's own responses (via <see cref="DockerEngineClient"/>) - deliberately <em>no</em>
/// <c>PropertyNamingPolicy</c>/<c>UseStringEnumConverter</c>, unlike every other
/// <c>*JsonContext</c> in this project: those describe Flare's own outbound wire format
/// (camelCase, string enums), while this one describes an external API's inbound JSON,
/// which is PascalCase and uses plain strings for state/health (see
/// <see cref="DockerContainerState"/>/<see cref="DockerContainerHealth"/>) - matching it
/// exactly, not Flare's own conventions, is the whole point.
/// </summary>
[JsonSerializable(typeof(DockerContainerSummary[]))]
[JsonSerializable(typeof(DockerContainerInspect))]
internal sealed partial class DockerApiJsonContext : JsonSerializerContext;
