namespace Flare.Api.Query;

/// <summary>
/// The ClickHouse quota key of the current request (ADR-0286): who the <c>flare_query</c> user's keyed quota
/// charges a query to, or null for none. Set per request by <c>ClickHouseQuotaMiddleware</c> and read by
/// <see cref="QuerySafety"/>, which sends it as the <see cref="HeaderName"/> header on every capped query.
/// Ambient for the same reason as <see cref="ServiceScope"/>: threading it through 50-odd services would leave
/// call sites to forget it. Background workers never set it, so their queries fall back to the user-name bucket.
/// </summary>
public static class ClickHouseQuotaKey
{
    /// <summary>The ClickHouse HTTP header that carries the quota key (<c>quota_key</c> is the older query parameter).</summary>
    public const string HeaderName = "X-ClickHouse-Quota";

    private static readonly AsyncLocal<string?> CurrentKey = new();

    public static string? Current
    {
        get => CurrentKey.Value;
        set => CurrentKey.Value = value;
    }

    /// <summary>The header to send with a query, or null when the request has no key.</summary>
    internal static IReadOnlyDictionary<string, string>? Headers() =>
        Current is { } key ? new Dictionary<string, string> { [HeaderName] = key } : null;
}
