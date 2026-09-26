using ClickHouse.Driver;
using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Query;

/// <summary>
/// The current <see cref="PromotedAttributeColumns"/> snapshots the log and span query
/// builders read (ADR-0062, ADR-0063). <see cref="Logs"/>/<see cref="Spans"/> are plain field
/// reads - never a ClickHouse round trip on the query path;
/// <see cref="PromotedAttributeRefreshWorker"/> keeps them fresh.
/// </summary>
public interface IPromotedAttributeRegistry
{
    PromotedAttributeColumns Logs { get; }

    PromotedAttributeColumns Spans { get; }

    Task RefreshAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Drops <paramref name="columnName"/> from <paramref name="table"/>'s snapshot right
    /// away - called before the <c>DROP COLUMN</c> runs, so this instance stops generating
    /// SQL against a column that's about to disappear.
    /// </summary>
    void Forget(PromotedAttributeTable table, string columnName);
}

public static class PromotedAttributeRegistryExtensions
{
    public static PromotedAttributeColumns For(this IPromotedAttributeRegistry registry, PromotedAttributeTable table) =>
        table == PromotedAttributeTable.Spans ? registry.Spans : registry.Logs;
}

/// <summary>
/// Reads promotions back from <c>system.columns</c> for the <c>logs</c> and <c>spans</c>
/// tables the query builders actually query (the Distributed tables in cluster mode, which
/// carry the same <c>MATERIALIZED</c> column - see <see cref="PromotedAttributeColumns.PromoteStatements"/>).
/// A failed refresh keeps the previous snapshots rather than dropping to empty: a transient
/// ClickHouse blip shouldn't silently slow every attribute filter back down.
/// </summary>
public sealed class PromotedAttributeRegistry(IClickHouseClient client, IOptions<QueryLimitsOptions> queryLimits, ILogger<PromotedAttributeRegistry> logger) : IPromotedAttributeRegistry
{
    private const string ColumnsSql = """
        SELECT table, name, default_expression
        FROM system.columns
        WHERE database = currentDatabase() AND table IN ('logs', 'spans')
          AND default_kind = 'MATERIALIZED' AND startsWith(name, 'attr_')
        ORDER BY table, name
        """;

    private volatile PromotedAttributeColumns _logs = PromotedAttributeColumns.Empty;
    private volatile PromotedAttributeColumns _spans = PromotedAttributeColumns.Empty;

    public PromotedAttributeColumns Logs => _logs;

    public PromotedAttributeColumns Spans => _spans;

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            var logs = new List<(AttributeBag, string, string)>();
            var spans = new List<(AttributeBag, string, string)>();
            await using var reader = await client.ExecuteReaderAsync(ColumnsSql, null, QuerySafety.ExecutionTimeOnly(queryLimits.Value), cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var table = reader.GetString(0) == "spans" ? PromotedAttributeTable.Spans : PromotedAttributeTable.Logs;
                var name = reader.GetString(1);
                if (PromotedAttributeColumns.TryParseExpression(table, reader.GetString(2), out var bag, out var key))
                {
                    (table == PromotedAttributeTable.Spans ? spans : logs).Add((bag, key, name));
                }
            }

            _logs = new PromotedAttributeColumns(logs);
            _spans = new PromotedAttributeColumns(spans);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Refreshing promoted attribute columns failed; keeping the previous {Count} column(s).", _logs.Count + _spans.Count);
        }
    }

    public void Forget(PromotedAttributeTable table, string columnName)
    {
        if (table == PromotedAttributeTable.Spans)
        {
            _spans = new PromotedAttributeColumns(_spans.All.Where(a => a.ColumnName != columnName));
        }
        else
        {
            _logs = new PromotedAttributeColumns(_logs.All.Where(a => a.ColumnName != columnName));
        }
    }
}

/// <summary>
/// Refreshes <see cref="IPromotedAttributeRegistry"/> at startup and every
/// <see cref="Interval"/> - how a promotion made through one Flare.Api instance reaches the
/// others and Flare.AlertWorker. The instance that runs the <c>ALTER</c> refreshes itself
/// immediately instead of waiting (see <see cref="PromotedAttributeAdminService"/>).
/// </summary>
public sealed class PromotedAttributeRefreshWorker(IPromotedAttributeRegistry registry) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            await registry.RefreshAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

public enum PromoteAttributeOutcome
{
    Promoted,
    AlreadyPromoted,
    InvalidKey,
    NameConflict,
    LimitReached,
}

public interface IPromotedAttributeAdminService
{
    Task<PromotedAttributesResponse> ListAsync(CancellationToken cancellationToken);

    Task<PromoteAttributeOutcome> PromoteAsync(PromoteAttributeRequest request, CancellationToken cancellationToken);

    /// <summary>False when <paramref name="columnName"/> isn't a currently promoted column on <paramref name="table"/>.</summary>
    Task<bool> DemoteAsync(PromotedAttributeTable table, string columnName, CancellationToken cancellationToken);
}

/// <summary>
/// Backs the Indexing page's "Promoted attributes" section: runs the promote/demote DDL
/// from <see cref="PromotedAttributeColumns"/> and reports backfill progress from
/// <c>system.mutations</c>. Every validation runs against a freshly refreshed snapshot, not
/// the cached one, so a promotion made on another instance a moment ago still counts.
/// </summary>
public sealed class PromotedAttributeAdminService(
    IClickHouseClient client,
    IPromotedAttributeRegistry registry,
    IOptions<QueryLimitsOptions> queryLimits,
    bool clusterMode) : IPromotedAttributeAdminService
{
    // Unfinished MATERIALIZE mutations on the storage tables - logs/spans single-node,
    // logs_local/spans_local (on this node) in cluster mode. Matched to a column by table and
    // name in ListAsync.
    private const string PendingMutationsSql = """
        SELECT table, command
        FROM system.mutations
        WHERE database = currentDatabase() AND table IN ('logs', 'logs_local', 'spans', 'spans_local') AND is_done = 0
        """;

    public async Task<PromotedAttributesResponse> ListAsync(CancellationToken cancellationToken)
    {
        await registry.RefreshAsync(cancellationToken);
        var pending = new List<(PromotedAttributeTable Table, string Command)>();
        await using (var reader = await client.ExecuteReaderAsync(PendingMutationsSql, null, QuerySafety.ExecutionTimeOnly(queryLimits.Value), cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var table = reader.GetString(0).StartsWith("spans", StringComparison.Ordinal) ? PromotedAttributeTable.Spans : PromotedAttributeTable.Logs;
                pending.Add((table, reader.GetString(1)));
            }
        }

        var attributes = new[] { PromotedAttributeTable.Logs, PromotedAttributeTable.Spans }
            .SelectMany(table => registry.For(table).All
                .OrderBy(a => a.Bag).ThenBy(a => a.Key, StringComparer.Ordinal)
                .Select(a =>
                {
                    var index = PromotedAttributeColumns.IndexNameFor(a.ColumnName);
                    var backfilling = pending.Any(p => p.Table == table
                        && (p.Command.Contains($"MATERIALIZE COLUMN {a.ColumnName}", StringComparison.Ordinal)
                            || p.Command.Contains($"MATERIALIZE INDEX {index}", StringComparison.Ordinal)));
                    return new PromotedAttributeInfo(a.Bag, a.Key, a.ColumnName, index, backfilling, table);
                }))
            .ToList();
        return new PromotedAttributesResponse(attributes, PromotedAttributeColumns.MaxPromotedAttributes);
    }

    public async Task<PromoteAttributeOutcome> PromoteAsync(PromoteAttributeRequest request, CancellationToken cancellationToken)
    {
        if (!PromotedAttributeColumns.IsValidKey(request.Key) || !Enum.IsDefined(request.Bag) || !Enum.IsDefined(request.Table))
        {
            return PromoteAttributeOutcome.InvalidKey;
        }

        await registry.RefreshAsync(cancellationToken);
        var current = registry.For(request.Table);
        if (current.TryGetColumn(request.Bag, request.Key, out _))
        {
            return PromoteAttributeOutcome.AlreadyPromoted;
        }

        var columnName = PromotedAttributeColumns.ColumnNameFor(request.Table, request.Bag, request.Key);
        if (current.All.Any(a => a.ColumnName == columnName))
        {
            return PromoteAttributeOutcome.NameConflict;
        }

        // The cap is per table - each one costs every insert into that table, not the other.
        if (current.Count >= PromotedAttributeColumns.MaxPromotedAttributes)
        {
            return PromoteAttributeOutcome.LimitReached;
        }

        foreach (var sql in PromotedAttributeColumns.PromoteStatements(request.Table, request.Bag, request.Key, clusterMode, request.Backfill))
        {
            await client.ExecuteNonQueryAsync(sql, null, null, cancellationToken);
        }

        await registry.RefreshAsync(cancellationToken);
        return PromoteAttributeOutcome.Promoted;
    }

    public async Task<bool> DemoteAsync(PromotedAttributeTable table, string columnName, CancellationToken cancellationToken)
    {
        await registry.RefreshAsync(cancellationToken);
        // Only a name already in the registry ever reaches the DDL text below.
        if (!Enum.IsDefined(table) || !registry.For(table).All.Any(a => a.ColumnName == columnName))
        {
            return false;
        }

        registry.Forget(table, columnName);
        foreach (var sql in PromotedAttributeColumns.DemoteStatements(table, columnName, clusterMode))
        {
            await client.ExecuteNonQueryAsync(sql, null, null, cancellationToken);
        }

        await registry.RefreshAsync(cancellationToken);
        return true;
    }
}
