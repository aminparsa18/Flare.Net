using System.Runtime.CompilerServices;
using System.Text.Json;
using MemoryPack;

namespace Flare.Api.Json;

/// <summary>
/// MemoryPack formatter for the opaque <see cref="JsonElement"/> saved-view state blob
/// (<c>Model.SavedView.State</c> / <c>Model.SavedViewRequest.State</c> - see those types'
/// remarks: dashboard-owned, never parsed by Flare.Api, only ever round-tripped through
/// ClickHouse's opaque <c>StateJson</c> column). MemoryPack has no native
/// <see cref="JsonElement"/> support (MEMPACK019) since it isn't one of MemoryPack's own
/// types - this is the "external type" escape hatch the MemoryPack docs'
/// <c>[MemoryPackAllowSerialize]</c> section describes, preserving the exact same
/// "opaque, unparsed-by-us" contract System.Text.Json already gives that member rather
/// than picking a different shape for it. Round-trips via raw JSON text, mirroring
/// <c>UriFormatter</c>'s "treat as a string" pattern in MemoryPack's own source.
/// </summary>
/// <remarks>
/// Phase 0 of docs-internal/investigations/memorypack-serialization-migration-scope.md -
/// this only makes <c>SavedView</c>/<c>SavedViewRequest</c> buildable with
/// <c>[MemoryPackable]</c> attached; no endpoint serializes via MemoryPack yet.
/// </remarks>
public sealed class JsonElementMemoryPackFormatter : MemoryPackFormatter<JsonElement>
{
    [ModuleInitializer]
    public static void RegisterFormatter()
    {
        MemoryPackFormatterProvider.Register(new JsonElementMemoryPackFormatter());
    }

    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref JsonElement value)
    {
        writer.WriteString(value.ValueKind == JsonValueKind.Undefined ? null : value.GetRawText());
    }

    public override void Deserialize(ref MemoryPackReader reader, scoped ref JsonElement value)
    {
        var raw = reader.ReadString();
        value = raw is null ? default : JsonDocument.Parse(raw).RootElement;
    }
}
