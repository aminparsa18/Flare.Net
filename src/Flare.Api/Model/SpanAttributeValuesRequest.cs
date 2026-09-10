using MemoryPack;

namespace Flare.Api.Model;

/// <summary>
/// Request body for <c>POST /api/spans/attribute-values</c> - the Traces page's equivalent
/// of <see cref="LogAttributeValuesRequest"/>, value autocomplete for
/// <see cref="SpanAttributeFilter"/>'s value field (see
/// <c>Query.SpanAttributeValuesQueryBuilder</c>). A separate type from
/// <see cref="LogAttributeValuesRequest"/>, not a shared/reused one - same
/// keep-logs-and-spans-independent reasoning <see cref="SpanAttributeBag"/>'s remarks give.
/// </summary>
[MemoryPackable]
public sealed partial record SpanAttributeValuesRequest
{
    /// <summary>See <see cref="LogSearchRequest.Filter"/>'s doc comment - the same JSON-deserialization caveat applies here.</summary>
    public SpanFilter Filter { get; init; } = new();

    /// <summary>Which bag <see cref="Key"/> is looked up in - same three bags <see cref="SpanAttributeFilter.Bag"/> targets.</summary>
    public SpanAttributeBag Bag { get; init; } = SpanAttributeBag.Span;

    /// <summary>The attribute key to enumerate observed values for.</summary>
    public required string Key { get; init; }

    /// <summary>Case-insensitive substring the caller has already typed, if any - same narrowing <see cref="LogAttributeValuesRequest.Prefix"/> documents.</summary>
    public string? Prefix { get; init; }

    /// <summary>Max distinct values returned, most-observed first.</summary>
    public int Limit { get; init; } = 25;
}

/// <summary>One distinct value observed for a <see cref="SpanAttributeValuesRequest.Key"/>, with how many in-scope spans carry it.</summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record SpanAttributeValueInfo
{
    public required string Value { get; init; }

    public required long Count { get; init; }
}

/// <summary>Response body for <c>POST /api/spans/attribute-values</c>, ordered by <see cref="SpanAttributeValueInfo.Count"/> descending.</summary>
[MemoryPackable]
public sealed partial record SpanAttributeValuesResponse
{
    public required IReadOnlyList<SpanAttributeValueInfo> Values { get; init; }
}
