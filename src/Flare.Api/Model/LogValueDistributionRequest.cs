namespace Flare.Api.Model;

/// <summary>Request body for <c>POST /api/logs/numeric-attribute-keys</c> - populates the Value distribution chart's attribute picker (see <c>Query.LogAttributeKeysQueryBuilder</c>).</summary>
public sealed record LogAttributeKeysRequest
{
    /// <summary>See <see cref="LogSearchRequest.Filter"/>'s doc comment - the same JSON-deserialization caveat applies here.</summary>
    public LogFilter Filter { get; init; } = new();
}

/// <summary>One <c>LogAttributes</c> key that parses as numeric on at least one in-scope event, with how many do.</summary>
public sealed record LogAttributeKeyInfo
{
    public required string Key { get; init; }

    public required long NumericCount { get; init; }
}

/// <summary>Response body for <c>POST /api/logs/numeric-attribute-keys</c>, ordered by <see cref="LogAttributeKeyInfo.NumericCount"/> descending.</summary>
public sealed record LogAttributeKeysResponse
{
    public required IReadOnlyList<LogAttributeKeyInfo> Keys { get; init; }
}

/// <summary>Request body for <c>POST /api/logs/value-distribution</c> - a random sample of one numeric <c>LogAttributes</c> value over time, for the Logs page's scatter/density chart.</summary>
public sealed record LogValueDistributionRequest
{
    /// <summary>See <see cref="LogSearchRequest.Filter"/>'s doc comment - the same JSON-deserialization caveat applies here.</summary>
    public LogFilter Filter { get; init; } = new();

    /// <summary>The <c>LogAttributes</c> key to sample - one of <see cref="LogAttributeKeyInfo.Key"/> from a prior <c>/api/logs/numeric-attribute-keys</c> call.</summary>
    public required string AttributeKey { get; init; }

    /// <summary>Max points returned. <c>ORDER BY rand() LIMIT n</c> - a uniform random sample, not the first n chronologically (see <see cref="Query.LogValueDistributionQueryBuilder"/>'s remarks on why that distinction matters here).</summary>
    public int SampleSize { get; init; } = 4000;
}

/// <summary>One sampled point: an event's <see cref="Value"/> for the requested attribute key, at the time it occurred.</summary>
public sealed record LogValueDistributionPoint
{
    public required DateTimeOffset Timestamp { get; init; }

    public required double Value { get; init; }
}

/// <summary>Response body for <c>POST /api/logs/value-distribution</c>.</summary>
public sealed record LogValueDistributionResponse
{
    public required IReadOnlyList<LogValueDistributionPoint> Points { get; init; }
}
