using System.Text.Json.Serialization;

namespace Flare.Api.LiveTail;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for
/// <see cref="BufferedLogEvent"/> - must parse the exact same wire bytes
/// <c>Flare.Ingest.Pipeline.LogEventJsonContext</c> produces (plain PascalCase, no naming
/// policy override, since both sides serialize/deserialize the same Redis Stream <c>data</c>
/// field). Deliberately not shared via project reference - see <see cref="BufferedLogEvent"/>.
/// </summary>
[JsonSerializable(typeof(BufferedLogEvent))]
public sealed partial class BufferedLogEventJsonContext : JsonSerializerContext;
