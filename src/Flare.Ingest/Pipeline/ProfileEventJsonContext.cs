using System.Text.Json.Serialization;
using Flare.Ingest.Model;

namespace Flare.Ingest.Pipeline;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for <see cref="ProfileSampleRecord"/>.
/// <see cref="RedisEventPayload.Decode{T}"/> requires a JSON type info for its pre-MemoryPack
/// fallback; no profiles entry was ever written as JSON, so this is only that signature's
/// requirement, mirroring <see cref="SpanEventJsonContext"/>.
/// </summary>
[JsonSerializable(typeof(ProfileSampleRecord))]
public sealed partial class ProfileEventJsonContext : JsonSerializerContext;
