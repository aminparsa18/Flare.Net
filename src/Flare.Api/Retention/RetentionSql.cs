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

/// <summary>A table's TTL: <see cref="Days"/> until rows are deleted (0 = never) and <see cref="ColdAfterDays"/> until parts move to the cold volume (0 = never). <see cref="PerResource"/> means the delete rule reads each row's <c>_retention_days</c> instead of one fixed number, and <see cref="Days"/> is then 0.</summary>
public readonly record struct TtlState(TtlKind Kind, int Days, int ColdAfterDays = 0, bool PerResource = false);

/// <summary>Pure SQL building/parsing for retention; the ClickHouse seam is <see cref="RetentionService"/>.</summary>
public static partial class RetentionSql
{
    public const string Database = "clickhousedb";
    public const string ClusterName = "flare_cluster";

    /// <summary>Storage policy from <c>db/clickhouse/config/cold-storage.xml</c>: volume <c>default</c> (local disk) then <see cref="ColdVolume"/>.</summary>
    public const string StoragePolicy = "flare_tiered";

    public const string ColdVolume = "cold";

    /// <summary>The per-row retention column (migration 0069) and its "keep forever" value.</summary>
    public const string RetentionColumn = "_retention_days";

    /// <summary>
    /// The <c>_retention_days</c> value that means "keep forever". Not larger on purpose: TTL expressions are <c>DateTime</c> arithmetic, and <c>DateTime</c> ends in
    /// 2106, so <c>now + 36500 days</c> wraps into the past and the TTL deletes the row. 20000 days
    /// (about 55 years) stays in range for any timestamp up to 2051.
    /// </summary>
    public const int ForeverDays = 20_000;

    /// <summary>Upper bound on a retention or cold-after value (50 years): the most that is safe inside <see cref="ForeverDays"/>'s <c>DateTime</c> range, and below it so "forever" can't be mistaken for a real value.</summary>
    public const int MaxDays = 18_250;

    private static string Target(string table, bool clusterMode) =>
        clusterMode ? $"{Database}.{table}_local ON CLUSTER '{ClusterName}'" : $"{Database}.{table}";

    /// <summary>
    /// <c>ALTER TABLE ... MODIFY TTL</c> (or <c>REMOVE TTL</c> when both are 0) for one data table:
    /// parts older than <paramref name="coldAfterDays"/> move to the cold volume, rows older than
    /// <paramref name="days"/> are deleted (or, with <paramref name="perResource"/>, as long as each row's own <c>_retention_days</c>). Either may be 0 on its own (tier-only keeps data forever).
    /// In cluster mode the statement targets the <c>_local</c> storage table <c>ON CLUSTER</c>: a
    /// <c>Distributed</c> table can't carry a TTL (ADR-0003). The caller must run it with
    /// <c>materialize_ttl_after_modify = 0</c> so existing parts are re-evaluated by background merges
    /// instead of being rewritten up front (<see cref="AlterSettings"/>).
    /// </summary>
    public static string BuildAlter(string table, string timeColumn, int days, bool clusterMode, int coldAfterDays = 0, bool perResource = false)
    {
        var rules = new List<string>(2);
        if (coldAfterDays > 0)
        {
            rules.Add($"toDateTime({timeColumn}) + toIntervalDay({coldAfterDays}) TO VOLUME '{ColdVolume}'");
        }

        if (perResource)
        {
            rules.Add($"toDateTime({timeColumn}) + toIntervalDay({RetentionColumn})");
        }
        else if (days > 0)
        {
            rules.Add($"toDateTime({timeColumn}) + toIntervalDay({days})");
        }

        return rules.Count == 0
            ? $"ALTER TABLE {Target(table, clusterMode)} REMOVE TTL"
            : $"ALTER TABLE {Target(table, clusterMode)} MODIFY TTL {string.Join(", ", rules)}";
    }

    /// <summary>
    /// The <c>MODIFY COLUMN _retention_days ... DEFAULT</c> statements for one table. Cluster mode needs
    /// two - the <c>_local</c> storage table and the <c>Distributed</c> one - because inserts through the
    /// Distributed table are filled with that table's own default (migration 0069).
    /// </summary>
    public static IReadOnlyList<string> BuildSetRetentionDefault(string table, string defaultExpression, bool clusterMode) =>
        clusterMode
            ?
            [
                $"ALTER TABLE {Database}.{table}_local ON CLUSTER '{ClusterName}' MODIFY COLUMN {RetentionColumn} UInt16 DEFAULT {defaultExpression}",
                $"ALTER TABLE {Database}.{table} ON CLUSTER '{ClusterName}' MODIFY COLUMN {RetentionColumn} UInt16 DEFAULT {defaultExpression}",
            ]
            : [$"ALTER TABLE {Database}.{table} MODIFY COLUMN {RetentionColumn} UInt16 DEFAULT {defaultExpression}"];

    /// <summary>Reads each data table's current <c>_retention_days</c> default expression.</summary>
    public static string BuildRetentionDefaultsSql(bool clusterMode) =>
        $"SELECT table, default_expression FROM system.columns WHERE database = '{Database}' AND name = '{RetentionColumn}' AND table IN ("
        + string.Join(", ", RetentionSignal.All.SelectMany(s => s.Tables).Select(t => $"'{(clusterMode ? t + "_local" : t)}'"))
        + ")";

    /// <summary>Assigns <see cref="StoragePolicy"/> to a table. Needed once per table before a TO VOLUME rule is accepted; idempotent.</summary>
    public static string BuildAssignPolicy(string table, bool clusterMode) =>
        $"ALTER TABLE {Target(table, clusterMode)} MODIFY SETTING storage_policy = '{StoragePolicy}'";

    /// <summary>The storage policy a data table currently has.</summary>
    public static string BuildCurrentPolicySql(string table, bool clusterMode) =>
        $"SELECT storage_policy FROM system.tables WHERE database = '{Database}' AND name = '{(clusterMode ? table + "_local" : table)}'";

    /// <summary>Whether the cold volume is configured on the connected node (the <c>cold-storage.xml</c> overlay is mounted).</summary>
    public static string ColdAvailableSql { get; } =
        $"SELECT count() FROM system.storage_policies WHERE policy_name = '{StoragePolicy}' AND volume_name = '{ColdVolume}'";

    public const string DisksSql = "SELECT name, type, free_space, total_space FROM system.disks ORDER BY name";

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

        var plain = FlareTtl().Match(clause.Groups["expr"].Value);
        if (!plain.Success || !(plain.Groups["days"].Success || plain.Groups["per"].Success || plain.Groups["cold"].Success))
        {
            return new TtlState(TtlKind.Custom, 0);
        }

        var days = plain.Groups["days"].Success ? int.Parse(plain.Groups["days"].Value) : 0;
        var cold = plain.Groups["cold"].Success ? int.Parse(plain.Groups["cold"].Value) : 0;
        return new TtlState(TtlKind.Days, days, cold, plain.Groups["per"].Success);
    }

    /// <summary>Collapses the per-table states of one signal: identical states stay as they are, anything else is <see cref="TtlKind.Custom"/> (mixed).</summary>
    public static TtlState Combine(IReadOnlyCollection<TtlState> states) =>
        states.Count > 0 && states.Distinct().Count() == 1 ? states.First() : new TtlState(TtlKind.Custom, 0);

    /// <summary>The expression of a <c>TTL</c> clause, up to a trailing <c>SETTINGS</c> (or the end).</summary>
    [GeneratedRegex(@"\sTTL\s+(?<expr>.+?)(?:\s+SETTINGS\s.*)?$", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex TtlClause();

    /// <summary>
    /// The two rule shapes Flare writes, in the order ClickHouse prints them: an optional
    /// <c>... TO VOLUME 'cold'</c> move rule, then an optional delete rule.
    /// </summary>
    [GeneratedRegex(@"^(?:toDateTime\(\w+\)\s*\+\s*toIntervalDay\((?<cold>\d+)\)\s+TO VOLUME 'cold'(?:,\s*)?)?(?:toDateTime\(\w+\)\s*\+\s*toIntervalDay\((?:(?<days>\d+)|(?<per>_retention_days))\))?$", RegexOptions.CultureInvariant)]
    private static partial Regex FlareTtl();
}
