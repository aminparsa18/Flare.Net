using System.Text.Json;
using System.Text.Json.Serialization;

namespace Flare.Mcp;

internal sealed class LogFilterWire
{
    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    public IReadOnlyList<string>? Services { get; init; }

    public IReadOnlyList<byte>? SeverityNumbers { get; init; }

    public string? TraceId { get; init; }

    public string? SpanId { get; init; }

    public string? PatternId { get; init; }

    public string? Search { get; init; }

    public IReadOnlyList<AttributeFilterWire>? Attributes { get; init; }

    public SpanFilterWire? TraceSpanFilter { get; init; }
}

/// <summary>
/// Hand-mirror of <c>Model/AttributeFilter.cs</c>. <see cref="Bag"/>/<see cref="Operator"/>
/// are plain strings (not a C# enum), matching this file's existing <c>StatusCodes</c>/
/// <c>Kinds</c>-as-strings convention in <see cref="TracesCommand"/>'s wire DTOs - avoids
/// needing a <c>JsonStringEnumConverter</c> on <see cref="WireJsonOptions"/>. Built only
/// via <see cref="Internal.AttributeFlagParsing"/>, which always fills <see cref="Bag"/>
/// with the default <c>"Log"</c> - no <c>--attr-bag</c> flag yet.
/// </summary>
internal sealed class AttributeFilterWire
{
    public string Bag { get; init; } = "Log";

    public required string Key { get; init; }

    public required string Value { get; init; }

    public string Operator { get; init; } = "Equals";
}

internal sealed class LogSearchRequestWire
{
    public LogFilterWire? Filter { get; init; }

    public string? Cursor { get; init; }

    public int? PageSize { get; init; }
}

/// <summary>
/// Full field set mirroring <c>LogEventDto</c>, even though <see cref="SearchCommand"/>'s
/// own table only renders a subset - <see cref="ExportCommand"/> needs the rest
/// (EventName/LogAttributes/etc.) and reuses this same type rather than a second
/// near-duplicate DTO.
/// </summary>
internal sealed class LogEventDtoWire
{
    public required Guid EventId { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public required string SeverityText { get; init; }

    public required byte SeverityNumber { get; init; }

    public required string ServiceName { get; init; }

    public required string Body { get; init; }

    public string TraceId { get; init; } = "";

    public string SpanId { get; init; } = "";

    public string EventName { get; init; } = "";

    public Dictionary<string, string> LogAttributes { get; init; } = [];
}

internal sealed class LogSearchResponseWire
{
    public List<LogEventDtoWire> Events { get; init; } = [];

    public string? NextCursor { get; init; }
}
