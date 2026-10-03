using System.Text.Json;
using System.Text.Json.Serialization;

namespace Flare.Mcp;

internal sealed class SpanFilterWire
{
    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    public IReadOnlyList<string>? Services { get; init; }

    public IReadOnlyList<byte>? Kinds { get; init; }

    public IReadOnlyList<string>? StatusCodes { get; init; }

    public string? TraceId { get; init; }

    public bool RootSpansOnly { get; init; }

    public ulong? MinDurationNano { get; init; }

    public ulong? MaxDurationNano { get; init; }

    public IReadOnlyList<SpanAttributeFilterWire>? Attributes { get; init; }

    public bool EntrySpansOnly { get; init; }

    public TraceStructureWire? Structure { get; init; }

    /// <summary>Exact span-name match (the facet sidebar's Name filter); used by the MCP run-comparison tool.</summary>
    public IReadOnlyList<string>? Names { get; init; }
}

/// <summary>Hand-mirror of <c>Model/TraceStructureModels.cs</c>'s <c>TraceStructureFilter</c>.</summary>
internal sealed class TraceStructureWire
{
    public required IReadOnlyList<TraceSpanConditionWire> Conditions { get; init; }

    public required string Expression { get; init; }
}

/// <summary>Hand-mirror of <c>TraceSpanCondition</c>, minus <c>Attributes</c> (no CLI flag for those yet).</summary>
internal sealed class TraceSpanConditionWire
{
    public required string Name { get; init; }

    public string? ServiceName { get; init; }

    public string? SpanName { get; init; }

    public string? StatusCode { get; init; }

    public ulong? MinDurationNano { get; init; }
}

/// <summary>
/// Hand-mirror of <c>Model/SpanFilter.cs</c>'s <c>SpanAttributeFilter</c>. <see cref="Bag"/>/
/// <see cref="Operator"/> are plain strings (not a C# enum), matching this file's existing
/// <c>StatusCodes</c>/<c>Kinds</c>-as-strings convention rather than a
/// <c>JsonStringEnumConverter</c> on <see cref="WireJsonOptions"/>. Built only via
/// <see cref="Internal.AttributeFlagParsing"/>, which always fills <see cref="Bag"/> with
/// the default <c>"Span"</c> - no <c>--attr-bag</c> flag yet.
/// </summary>
internal sealed class SpanAttributeFilterWire
{
    public string Bag { get; init; } = "Span";

    public required string Key { get; init; }

    public required string Value { get; init; }

    public string Operator { get; init; } = "Equals";
}

internal sealed class SpanSearchRequestWire
{
    public SpanFilterWire? Filter { get; init; }

    public string? Cursor { get; init; }

    public int? PageSize { get; init; }

    /// <summary><c>SpanSortKey</c> member name - <c>StartTime</c>/<c>Duration</c>/<c>SpanCount</c>. Plain string, same convention as <see cref="SpanAttributeFilterWire.Operator"/>.</summary>
    public string SortBy { get; init; } = "StartTime";

    public bool SortAscending { get; init; }
}

internal sealed class SpanDtoWire
{
    public required string TraceId { get; init; }

    // Empty string = root span - same "empty string means absent" convention traces-api.ts
    // documents. Not `required`: SpanCount is search-only and SpanId/ParentSpanId/EndTime/
    // Kind are unused by TracesCommand's flat list, so leaving them defaultable keeps this
    // one DTO shared by both the search (SpanSearchResponseWire) and get-trace
    // (TraceDtoWire) responses without either caller needing fields it doesn't read.
    public string SpanId { get; init; } = "";

    public string ParentSpanId { get; init; } = "";

    public required string ServiceName { get; init; }

    public required string Name { get; init; }

    /// <summary>OTel SpanKind: 0=unspecified, 1=internal, 2=server, 3=client, 4=producer, 5=consumer.</summary>
    public int Kind { get; init; }

    public DateTimeOffset StartTime { get; init; }

    public DateTimeOffset EndTime { get; init; }

    public ulong DurationNano { get; init; }

    public required string StatusCode { get; init; }

    public int? SpanCount { get; init; }

    // Populated the same way and under the same condition as SpanCount (root-span search
    // results only) - see Flare.Api's SpanDto.HasError remarks. Rolled into the printed
    // status by RolledUpStatusCode rather than shown as its own column.
    public bool? HasError { get; init; }
}

internal sealed class SpanSearchResponseWire
{
    public List<SpanDtoWire> Spans { get; init; } = [];

    public string? NextCursor { get; init; }
}

/// <summary>Wire shape of <c>GET /api/traces/{traceId}</c>'s response (<c>TraceDto</c> - see traces-api.ts).</summary>
internal sealed class TraceDtoWire
{
    public required string TraceId { get; init; }

    /// <summary>Ascending by StartTime - same ordering guarantee traces-api.ts documents.</summary>
    public List<SpanDtoWire> Spans { get; init; } = [];

    /// <summary>True when the API hit its per-trace span cap and <see cref="Spans"/> is only the earliest ones.</summary>
    public bool Truncated { get; init; }
}

internal static class WireJsonOptions
{
    public static readonly JsonSerializerOptions Instance = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}
