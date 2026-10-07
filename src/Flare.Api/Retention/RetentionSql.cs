using System.Text.RegularExpressions;

namespace Flare.Api.Retention;

/// <summary>What a data table's <c>create_table_query</c> says about its TTL.</summary>
public enum TtlKind
{
    /// <summary>No TTL clause: rows are kept forever.</summary>
    None,

    /// <summary>A plain "<c>time + N days</c>" TTL, the only shape Flare writes.</summary>
    Days,

    /// <summary>A TTL clause Flare did not write (hand-edited, or a tiered/<c>TO VOLUME</c> rule).</summary>
    Custom,
}

public readonly record struct TtlState(TtlKind Kind, int Days);

/// <summary>Pure SQL building/parsing for retention; the ClickHouse seam is <see cref="RetentionService"/>.</summary>
public static partial class RetentionSql
{
    public const string Database = "clickhousedb";
    public const string ClusterName = "flare_cluster";

    /// <summary>Upper bound (100 years) purely to reject fat-fingered input.</summary>
    public const int MaxDays = 36_500;

    /// <summary>
    /// <c>ALTER TABLE ... MODIFY TTL</c> (or <c>REMOVE TTL</c> for <paramref name="days"/> = 0) for one data table.
    /// In cluster mode the statement targets the <c>_local</c> storage table <c>ON CLUSTER</c>: a
    /// <c>Distributed</c> table can't carry a TTL (ADR-0003). The caller must run it with
    /// <c>materialize_ttl_after_modify = 0</c> so existing parts are re-evaluated by background merges
    /// instead of being rewritten up front (<see cref="AlterSettings"/>).
    /// </summary>
    public static string BuildAlter(string table, string timeColumn, int days, bool clusterMode)
    {
        var target = clusterMode ? $"{Database}.{table}_local ON CLUSTER '{ClusterName}'" : $"{Database}.{table}";
        return days <= 0
            ? $"ALTER TABLE {target} REMOVE TTL"
            : $"ALTER TABLE {target} MODIFY TTL toDateTime({timeColumn}) + toIntervalDay({days})";
    }

    /// <summary>Query settings every retention <c>ALTER</c> runs with.</summary>
    public static IReadOnlyDictionary<string, object> AlterSettings { get; } = new Dictionary<string, object>
    {
        ["materialize_ttl_after_modify"] = 0,
        ["distributed_ddl_task_timeout"] = 600,
        ["max_execution_time"] = 600,
    };

    /// <summary>Reads each data table's <c>create_table_query</c>; in cluster mode the <c>_local</c> storage table is the one that holds the TTL.</summary>
    public static string BuildCreateQueriesSql(bool clusterMode) =>
        $"SELECT name, create_table_query FROM system.tables WHERE database = '{Database}' AND name IN ("
        + string.Join(", ", RetentionSignal.All.SelectMany(s => s.Tables).Select(t => $"'{(clusterMode ? t + "_local" : t)}'"))
        + ")";

    /// <summary>
    /// Parses the table-level TTL out of a <c>create_table_query</c>. ClickHouse stores that DDL on a
    /// single line and normalises the expression to <c>toIntervalDay(N)</c>. Only the part after
    /// <c>ENGINE =</c> is looked at, so a column-level TTL is never mistaken for the table's.
    /// </summary>
    public static TtlState ParseTtl(string createTableQuery)
    {
        var engineIndex = createTableQuery.IndexOf("ENGINE =", StringComparison.Ordinal);
        var tail = engineIndex < 0 ? createTableQuery : createTableQuery[engineIndex..];

        var clause = TtlClause().Match(tail);
        if (!clause.Success)
        {
            return new TtlState(TtlKind.None, 0);
        }

        var plain = PlainDaysTtl().Match(clause.Groups["expr"].Value);
        return plain.Success && int.TryParse(plain.Groups["days"].ValueSpan, out var days)
            ? new TtlState(TtlKind.Days, days)
            : new TtlState(TtlKind.Custom, 0);
    }

    /// <summary>Collapses the per-table states of one signal: identical states stay as they are, anything else is <see cref="TtlKind.Custom"/> (mixed).</summary>
    public static TtlState Combine(IReadOnlyCollection<TtlState> states) =>
        states.Count > 0 && states.Distinct().Count() == 1 ? states.First() : new TtlState(TtlKind.Custom, 0);

    /// <summary>The expression of a <c>TTL</c> clause, up to a trailing <c>SETTINGS</c> (or the end).</summary>
    [GeneratedRegex(@"\sTTL\s+(?<expr>.+?)(?:\s+SETTINGS\s.*)?$", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex TtlClause();

    [GeneratedRegex(@"^toDateTime\(\w+\)\s*\+\s*toIntervalDay\((?<days>\d+)\)$", RegexOptions.CultureInvariant)]
    private static partial Regex PlainDaysTtl();
}
