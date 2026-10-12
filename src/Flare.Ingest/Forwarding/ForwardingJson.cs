using System.Text.Json.Serialization;

namespace Flare.Ingest.Forwarding;

/// <summary>Serialises a target's options to detect that it changed between refreshes.</summary>
[JsonSerializable(typeof(ForwardingTargetOptions))]
internal sealed partial class ForwardingJson : JsonSerializerContext;
