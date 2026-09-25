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

    /// <summary>The attribute key to enumerate observed values for. Ignored (may be empty) unless <see cref="Field"/> is <see cref="SpanValuesField.Attribute"/>.</summary>
    public string Key { get; init; } = "";

    /// <summary>Case-insensitive substring the caller has already typed, if any - same narrowing <see cref="LogAttributeValuesRequest.Prefix"/> documents.</summary>
    public string? Prefix { get; init; }

    /// <summary>Max distinct values returned, most-observed first.</summary>
    public int Limit { get; init; } = 25;

    /// <summary>What to enumerate - see <see cref="LogAttributeValuesRequest.Field"/>; the Traces facet sidebar's built-in sections.</summary>
    public SpanValuesField Field { get; init; } = SpanValuesField.Attribute;
}

/// <summary>Source column for <see cref="SpanAttributeValuesRequest.Field"/>.</summary>
public enum SpanValuesField
{
    /// <summary><see cref="SpanAttributeValuesRequest.Bag"/>[<see cref="SpanAttributeValuesRequest.Key"/>].</summary>
    Attribute,

    /// <summary><c>ServiceName</c>.</summary>
    Service,

    /// <summary><c>StatusCode</c>'s Enum8 label, e.g. <c>STATUS_CODE_ERROR</c> - same form <see cref="SpanFilter.StatusCodes"/> takes.</summary>
    Status,

    /// <summary><c>Kind</c> (OTel SpanKind 0-5), as its decimal string.</summary>
    Kind,

    /// <summary><c>Name</c> (the operation) - same form <see cref="SpanFilter.Names"/> takes.</summary>
    Name,

    /// <summary>
    /// <c>DurationNano</c> bucketed by <c>Query.SpanAttributeValuesQueryBuilder.DurationBucketLowerBoundsNano</c>,
    /// each value the bucket's inclusive lower bound in nanoseconds as a decimal string.
    /// </summary>
    DurationBucket,
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
