namespace Flare.Api.Query;

/// <summary>
/// Old/new OpenTelemetry semantic-convention spellings of the same span attribute
/// (e.g. <c>server.address</c> / <c>net.peer.name</c>). Mixed .NET instrumentation
/// versions emit either, so a span filter on one spelling expands to all of them. Only
/// pairs whose values are interchangeable are listed.
/// </summary>
public static class SemconvAliases
{
    private static readonly string[][] Groups =
    [
        ["server.address", "net.peer.name"],
        ["server.port", "net.peer.port"],
        ["url.full", "http.url"],
        ["url.scheme", "http.scheme"],
        ["http.request.method", "http.method"],
        ["http.response.status_code", "http.status_code"],
        ["user_agent.original", "http.user_agent"],
        ["db.system.name", "db.system"],
        ["db.operation.name", "db.operation"],
        ["db.namespace", "db.name"],
        ["db.query.text", "db.statement"],
    ];

    private static readonly Dictionary<string, IReadOnlyList<string>> ByKey =
        Groups.SelectMany(g => g.Select(k => (k, g))).ToDictionary(x => x.k, x => (IReadOnlyList<string>)x.g, StringComparer.Ordinal);

    /// <summary>Every spelling of <paramref name="key"/> (the key itself first), or null if it has no alias.</summary>
    public static IReadOnlyList<string>? Group(string key)
    {
        if (!ByKey.TryGetValue(key, out var group))
        {
            return null;
        }

        return [key, .. group.Where(k => k != key)];
    }
}
