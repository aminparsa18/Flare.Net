using System.Text.Json.Serialization;

namespace Flare.Ingest.Patterns;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for a bucket's
/// <see cref="ClusterRecord"/> list, matching <see cref="Pipeline.LogEventJsonContext"/>'s
/// precedent for Redis-stored blobs. Used only by <see cref="RedisPatternClusterStore"/> -
/// <see cref="InMemoryPatternClusterStore"/> never serializes.
/// </summary>
[JsonSerializable(typeof(ClusterRecord[]))]
public sealed partial class PatternClusterRecordJsonContext : JsonSerializerContext;
