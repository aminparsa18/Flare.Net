using System.Text.Json.Serialization;

namespace Flare.Ingest.Stats;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for <see cref="IngestionErrorEntry"/>,
/// matching <see cref="Pipeline.LogEventJsonContext"/>'s precedent for Redis-stored blobs.
/// </summary>
[JsonSerializable(typeof(IngestionErrorEntry))]
public sealed partial class IngestionErrorEntryJsonContext : JsonSerializerContext;
