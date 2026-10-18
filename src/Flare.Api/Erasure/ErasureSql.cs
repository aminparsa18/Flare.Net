using System.Security.Cryptography;
using System.Text;

namespace Flare.Api.Erasure;

/// <summary>
/// Pure SQL building for data-subject erasure and export (docs-internal/adr/0198-data-subject-erasure-export.md);
/// the ClickHouse seam is <see cref="ErasureService"/>. Values are rendered as escaped literals, not
/// query parameters: an <c>ALTER ... DELETE</c> stores its command text in <c>system.mutations</c> and
/// is re-run by background merges, so it must be self-contained.
/// </summary>
public static class ErasureSql
{
    public const string Database = "clickhousedb";
    public const string ClusterName = "flare_cluster";

    public const int MaxIdentifiers = 10;

    /// <summary>Sessions followed from the identifiers into <c>app_screenshots</c>/<c>app_feedback</c>.</summary>
    public const int MaxSessions = 10_000;

    public const string SessionAttribute = "session.id";

    /// <summary>
    /// How a table is matched. <see cref="AttributeMaps"/> are <c>Map(String, String)</c> columns searched for
    /// <c>map[key] = value</c>; <see cref="EventAttributes"/> is the span <c>Events.Attributes</c> array of maps (exception
    /// events); <see cref="UsesSessions"/> adds <c>SessionId IN (...)</c>; <see cref="SessionMap"/> names the attribute map whose <c>session.id</c> is matched against the subject's sessions; <see cref="UserIdColumn"/>/<see cref="EmailColumn"/>
    /// are plain columns matched when the identifier key says they hold that value.
    /// </summary>
    public sealed record ErasureTable(
        string Name,
        IReadOnlyList<string> AttributeMaps,
        bool EventAttributes = false,
        bool UsesSessions = false,
        string? SessionMap = null,
        string? UserIdColumn = null,
        string? EmailColumn = null);

    /// <summary>Every table that can hold a data subject's raw rows. The pre-aggregated rollups hold counts and quantiles with no identifiers, and the config tables are not telemetry.</summary>
    public static IReadOnlyList<ErasureTable> Tables { get; } =
    [
        new("logs", ["ResourceAttributes", "ScopeAttributes", "LogAttributes"], SessionMap: "LogAttributes"),
        new("spans", ["ResourceAttributes", "ScopeAttributes", "SpanAttributes"], EventAttributes: true, SessionMap: "SpanAttributes"),
        new("metrics_gauge", ["ResourceAttributes", "ScopeAttributes", "DataPointAttributes"], SessionMap: "DataPointAttributes"),
        new("metrics_sum", ["ResourceAttributes", "ScopeAttributes", "DataPointAttributes"], SessionMap: "DataPointAttributes"),
        new("metrics_histogram", ["ResourceAttributes", "ScopeAttributes", "DataPointAttributes"], SessionMap: "DataPointAttributes"),
        new("metrics_exponential_histogram", ["ResourceAttributes", "ScopeAttributes", "DataPointAttributes"], SessionMap: "DataPointAttributes"),
        new("metric_exemplars", ["ResourceAttributes", "DataPointAttributes", "FilteredAttributes"], SessionMap: "DataPointAttributes"),
        new("profile_samples", ["ResourceAttributes", "SampleAttributes"]),
        new("app_screenshots", [], UsesSessions: true),
        new("app_feedback", [], UsesSessions: true, UserIdColumn: "UserId", EmailColumn: "Email"),
    ];

    private static readonly HashSet<string> UserIdKeys = new(StringComparer.Ordinal) { "user.id", "enduser.id" };
    private static readonly HashSet<string> EmailKeys = new(StringComparer.Ordinal) { "user.email", "enduser.email" };

    public static string? ValidateIdentifier(string? key, string? value)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 200)
        {
            return "Identifier key is required (at most 200 characters).";
        }

        if (string.IsNullOrEmpty(value) || value.Length > 1000)
        {
            return $"Identifier value for '{key}' is required (at most 1000 characters). An empty value would match every row without the attribute.";
        }

        if (HasControlCharacters(key) || HasControlCharacters(value))
        {
            return $"Identifier '{key}' contains control characters.";
        }

        return null;
    }

    private static bool HasControlCharacters(string s) => s.Any(char.IsControl);

    /// <summary>ClickHouse string literal: backslash and single quote escaped.</summary>
    public static string Literal(string value) => "'" + value.Replace("\\", "\\\\").Replace("'", "\\'") + "'";

    /// <summary>
    /// The WHERE condition for <paramref name="table"/>: any identifier matches in any of the table's attribute maps (or its
    /// plain columns), or its <c>SessionId</c> is one of <paramref name="sessionIds"/>. <c>0</c> when nothing can match.
    /// </summary>
    public static string BuildPredicate(ErasureTable table, IReadOnlyList<ErasureIdentifier> identifiers, IReadOnlyList<string> sessionIds)
    {
        var terms = new List<string>();
        foreach (var identifier in identifiers)
        {
            var key = Literal(identifier.Key);
            var value = Literal(identifier.Value);
            terms.AddRange(table.AttributeMaps.Select(map => $"{map}[{key}] = {value}"));
            if (table.EventAttributes)
            {
                terms.Add($"arrayExists(m -> m[{key}] = {value}, `Events.Attributes`)");
            }

            if (table.UserIdColumn is { } userId && UserIdKeys.Contains(identifier.Key))
            {
                terms.Add($"{userId} = {value}");
            }

            if (table.EmailColumn is { } email && EmailKeys.Contains(identifier.Key))
            {
                terms.Add($"{email} = {value}");
            }
        }

        if (sessionIds.Count > 0)
        {
            var list = string.Join(", ", sessionIds.Select(Literal));
            if (table.UsesSessions)
            {
                terms.Add($"SessionId IN ({list})");
            }

            if (table.SessionMap is { } map)
            {
                terms.Add($"{map}[{Literal(SessionAttribute)}] IN ({list})");
            }
        }

        return terms.Count == 0 ? "0" : string.Join(" OR ", terms);
    }

    /// <summary>Session ids reachable from the identifiers through a table's <c>session.id</c> attribute. Only <c>logs</c> and <c>spans</c> carry one.</summary>
    public static string BuildSessionIdsSql(ErasureTable table, IReadOnlyList<ErasureIdentifier> identifiers)
    {
        var sessionMap = table.Name == "spans" ? "SpanAttributes" : "LogAttributes";
        var session = Literal(SessionAttribute);
        return $"SELECT DISTINCT {sessionMap}[{session}] AS s FROM {table.Name} WHERE ({BuildPredicate(table, identifiers, [])}) AND s != '' LIMIT {MaxSessions + 1}";
    }

    public static string BuildCountSql(ErasureTable table, IReadOnlyList<ErasureIdentifier> identifiers, IReadOnlyList<string> sessionIds) =>
        $"SELECT count() FROM {table.Name} WHERE {BuildPredicate(table, identifiers, sessionIds)}";

    /// <summary>
    /// One <c>formatRow</c> line per matching row (<c>JSONEachRow</c>, so maps, arrays and timestamps need no custom
    /// serialization). <c>*</c> leaves out MATERIALIZED columns, which are derived from the rest.
    /// </summary>
    public static string BuildExportSql(ErasureTable table, IReadOnlyList<ErasureIdentifier> identifiers, IReadOnlyList<string> sessionIds, int rowLimit) =>
        $"SELECT formatRow('JSONEachRow', *) FROM {table.Name} WHERE {BuildPredicate(table, identifiers, sessionIds)} LIMIT {rowLimit}";

    /// <summary>
    /// The mutation. <paramref name="transactionId"/> is embedded as an always-true comparison so the command text in
    /// <c>system.mutations</c> carries it and <see cref="BuildMutationProgressSql"/> can find this request's mutations: an
    /// <c>ALTER</c> that does not wait for its mutation returns no mutation id.
    /// </summary>
    public static string BuildDelete(ErasureTable table, IReadOnlyList<ErasureIdentifier> identifiers, IReadOnlyList<string> sessionIds, Guid transactionId, bool clusterMode)
    {
        var target = clusterMode ? $"{Database}.{table.Name}_local ON CLUSTER '{ClusterName}'" : $"{Database}.{table.Name}";
        return $"ALTER TABLE {target} DELETE WHERE ({BuildPredicate(table, identifiers, sessionIds)}) AND {Literal(transactionId.ToString())} != ''";
    }

    /// <summary>Per table: mutations found, finished, parts left, and the first failure reason.</summary>
    public static string BuildMutationProgressSql(Guid transactionId, bool clusterMode)
    {
        var source = clusterMode ? $"clusterAllReplicas('{ClusterName}', system.mutations)" : "system.mutations";
        return $"""
            SELECT table, count() AS total, countIf(is_done) AS done, sum(parts_to_do) AS parts, anyIf(latest_fail_reason, latest_fail_reason != '') AS reason
            FROM {source}
            WHERE database = '{Database}' AND position(command, {Literal(transactionId.ToString())}) > 0
            GROUP BY table
            """;
    }

    /// <summary>First 16 hex characters of SHA-256 over the identifiers, order-independent.</summary>
    public static string SubjectHash(IReadOnlyList<ErasureIdentifier> identifiers)
    {
        var canonical = string.Join('\n', identifiers.Select(i => $"{i.Key}={i.Value}").Order(StringComparer.Ordinal));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..16].ToLowerInvariant();
    }
}
