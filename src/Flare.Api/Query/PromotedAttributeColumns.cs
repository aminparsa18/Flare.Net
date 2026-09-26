using System.Text;
using System.Text.RegularExpressions;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>
/// Immutable snapshot of which log attribute keys have been promoted to their own
/// <c>MATERIALIZED</c> column (ADR-0062), plus the pure naming/DDL/parsing rules behind
/// promotion. <see cref="LogFilterSqlBuilder"/> consults a snapshot to read
/// <c>attr_log_http_route</c> instead of <c>LogAttributes['http.route']</c>; the snapshot
/// itself comes from <c>system.columns</c> (see <see cref="PromotedAttributeRegistry"/>) -
/// the table's own schema is the registry, so it can never disagree with the DDL that
/// actually ran.
/// </summary>
/// <remarks>
/// DDL can't take bound parameters, so a promoted key is spliced into the <c>ALTER</c>
/// text as a string literal. <see cref="IsValidKey"/> is what makes that safe: it admits
/// only characters that need no escaping inside a single-quoted ClickHouse literal (no
/// quote, no backslash), which also covers every OTel semantic-convention key.
/// </remarks>
public sealed partial class PromotedAttributeColumns
{
    /// <summary>Prefix every promoted column name starts with - how <see cref="PromotedAttributeRegistry"/> tells them apart from schema columns.</summary>
    public const string ColumnPrefix = "attr_";

    /// <summary>Cap on promoted keys - each one is a real column plus a skip index written on every insert.</summary>
    public const int MaxPromotedAttributes = 50;

    public const int MaxKeyLength = 200;

    public static readonly PromotedAttributeColumns Empty = new([]);

    private readonly Dictionary<(AttributeBag Bag, string Key), string> _columns;

    public PromotedAttributeColumns(IEnumerable<(AttributeBag Bag, string Key, string ColumnName)> columns)
    {
        _columns = new Dictionary<(AttributeBag, string), string>();
        foreach (var (bag, key, columnName) in columns)
        {
            _columns[(bag, key)] = columnName;
        }
    }

    public int Count => _columns.Count;

    public IEnumerable<(AttributeBag Bag, string Key, string ColumnName)> All =>
        _columns.Select(kv => (kv.Key.Bag, kv.Key.Key, kv.Value));

    public bool TryGetColumn(AttributeBag bag, string key, out string columnName) =>
        _columns.TryGetValue((bag, key), out columnName!);

    [GeneratedRegex(@"^[A-Za-z0-9_.\-:/@]+$")]
    private static partial Regex KeyPattern();

    // ClickHouse's canonical formatting of the MATERIALIZED expression, as system.columns'
    // default_expression reports it (confirmed live): LogAttributes['http.route'].
    [GeneratedRegex(@"^(LogAttributes|ResourceAttributes|ScopeAttributes)\['([^'\\]+)'\]$")]
    private static partial Regex ExpressionPattern();

    public static bool IsValidKey(string? key) =>
        !string.IsNullOrEmpty(key) && key.Length <= MaxKeyLength && KeyPattern().IsMatch(key);

    /// <summary>
    /// <c>attr_{bag}_{key}</c> with every character outside <c>[A-Za-z0-9_]</c> replaced by
    /// <c>_</c>: <c>(Log, "http.route")</c> -> <c>attr_log_http_route</c>. Not injective
    /// (<c>http.route</c> and <c>http_route</c> collide) - the promote endpoint rejects a
    /// key whose name is already taken by a different key.
    /// </summary>
    public static string ColumnNameFor(AttributeBag bag, string key)
    {
        var sb = new StringBuilder(ColumnPrefix).Append(BagPrefix(bag)).Append('_');
        foreach (var c in key)
        {
            sb.Append(char.IsAsciiLetterOrDigit(c) || c == '_' ? c : '_');
        }

        return sb.ToString();
    }

    public static string IndexNameFor(string columnName) => $"idx_{columnName}";

    /// <summary>
    /// Reverses the <c>MATERIALIZED</c> expression <see cref="PromoteStatements"/> writes,
    /// from a <c>system.columns.default_expression</c> value. False for anything else, so a
    /// hand-added <c>attr_*</c> column with a different expression is ignored rather than
    /// misread as a promotion.
    /// </summary>
    public static bool TryParseExpression(string expression, out AttributeBag bag, out string key)
    {
        var match = ExpressionPattern().Match(expression.Trim());
        if (!match.Success)
        {
            bag = default;
            key = string.Empty;
            return false;
        }

        bag = match.Groups[1].Value switch
        {
            "ResourceAttributes" => AttributeBag.Resource,
            "ScopeAttributes" => AttributeBag.Scope,
            _ => AttributeBag.Log,
        };
        key = match.Groups[2].Value;
        return true;
    }

    /// <summary>
    /// The <c>ALTER</c> statements that promote <paramref name="key"/>, in execution order.
    /// Single-node: one <c>ALTER</c> on <c>logs</c> adding the column and its skip index
    /// together. Cluster mode (ADR-0062): the same on <c>logs_local</c> <c>ON CLUSTER</c>,
    /// then the column alone on the <c>logs</c> Distributed table - local first so the
    /// Distributed table never exposes a column its shards don't have yet (same two-table
    /// shape as <c>db/clickhouse-cluster/0010_logs_pattern.sql</c>). The optional
    /// <c>MATERIALIZE</c> mutations go last and run asynchronously.
    /// </summary>
    public static IReadOnlyList<string> PromoteStatements(AttributeBag bag, string key, bool clusterMode, bool backfill)
    {
        EnsureValidKey(key);
        var column = ColumnNameFor(bag, key);
        var index = IndexNameFor(column);
        var columnDef = $"{column} String MATERIALIZED {LogFilterSqlBuilder.ColumnFor(bag)}['{key}'] CODEC(ZSTD(1))";
        var storage = clusterMode ? "logs_local ON CLUSTER 'flare_cluster'" : "logs";

        var statements = new List<string>
        {
            $"ALTER TABLE {storage} ADD COLUMN IF NOT EXISTS {columnDef}, " +
            $"ADD INDEX IF NOT EXISTS {index} {column} TYPE bloom_filter(0.01) GRANULARITY 1",
        };
        if (clusterMode)
        {
            statements.Add($"ALTER TABLE logs ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS {columnDef}");
        }

        if (backfill)
        {
            statements.Add($"ALTER TABLE {storage} MATERIALIZE COLUMN {column}");
            statements.Add($"ALTER TABLE {storage} MATERIALIZE INDEX {index}");
        }

        return statements;
    }

    /// <summary>
    /// Reverse of <see cref="PromoteStatements"/>: Distributed table first (cluster mode),
    /// then the index before the column it's built on. <paramref name="columnName"/> must
    /// already be a known promoted column - it's spliced into DDL.
    /// </summary>
    public static IReadOnlyList<string> DemoteStatements(string columnName, bool clusterMode)
    {
        var index = IndexNameFor(columnName);
        var storage = clusterMode ? "logs_local ON CLUSTER 'flare_cluster'" : "logs";
        var statements = new List<string>();
        if (clusterMode)
        {
            statements.Add($"ALTER TABLE logs ON CLUSTER 'flare_cluster' DROP COLUMN IF EXISTS {columnName}");
        }

        statements.Add($"ALTER TABLE {storage} DROP INDEX IF EXISTS {index}");
        statements.Add($"ALTER TABLE {storage} DROP COLUMN IF EXISTS {columnName}");
        return statements;
    }

    private static void EnsureValidKey(string key)
    {
        if (!IsValidKey(key))
        {
            throw new ArgumentException($"'{key}' is not a promotable attribute key.", nameof(key));
        }
    }

    private static string BagPrefix(AttributeBag bag) => bag switch
    {
        AttributeBag.Resource => "res",
        AttributeBag.Scope => "scope",
        _ => "log",
    };
}
