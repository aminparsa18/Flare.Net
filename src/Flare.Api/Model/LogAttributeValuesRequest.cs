using MemoryPack;

namespace Flare.Api.Model;

/// <summary>
/// Request body for <c>POST /api/logs/attribute-values</c> - value autocomplete for
/// <see cref="AttributeFilter"/>'s value field (see
/// <c>Query.LogAttributeValuesQueryBuilder</c>). Unlike
/// <see cref="LogAttributeKeysRequest"/> (which enumerates numeric-looking keys for the
/// Value distribution chart), this enumerates the *values* actually observed for one
/// caller-supplied key, regardless of type - the Attribute filters builder's "suggest
/// values for this attribute key" affordance, same one <see cref="AttributeBag"/>
/// three-bag shape <see cref="AttributeFilter"/> already targets.
/// </summary>
[MemoryPackable]
public sealed partial record LogAttributeValuesRequest
{
    /// <summary>See <see cref="LogSearchRequest.Filter"/>'s doc comment - the same JSON-deserialization caveat applies here.</summary>
    public LogFilter Filter { get; init; } = new();

    /// <summary>Which bag <see cref="Key"/> is looked up in - same three bags <see cref="AttributeFilter.Bag"/> targets.</summary>
    public AttributeBag Bag { get; init; } = AttributeBag.Log;

    /// <summary>The attribute key to enumerate observed values for.</summary>
    public required string Key { get; init; }

    /// <summary>
    /// Case-insensitive substring the caller has already typed, if any - narrows candidates
    /// server-side (<c>ILIKE '%text%'</c>, same operator <see cref="LogFilter.Search"/> uses)
    /// instead of shipping every distinct value for a high-cardinality key. Empty/null = no
    /// narrowing - every observed value is a candidate (still capped by <see cref="Limit"/>).
    /// </summary>
    public string? Prefix { get; init; }

    /// <summary>Max distinct values returned, most-observed first.</summary>
    public int Limit { get; init; } = 25;
}

/// <summary>One distinct value observed for a <see cref="LogAttributeValuesRequest.Key"/>, with how many in-scope events carry it.</summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record LogAttributeValueInfo
{
    public required string Value { get; init; }

    public required long Count { get; init; }
}

/// <summary>Response body for <c>POST /api/logs/attribute-values</c>, ordered by <see cref="LogAttributeValueInfo.Count"/> descending.</summary>
[MemoryPackable]
public sealed partial record LogAttributeValuesResponse
{
    public required IReadOnlyList<LogAttributeValueInfo> Values { get; init; }
}
