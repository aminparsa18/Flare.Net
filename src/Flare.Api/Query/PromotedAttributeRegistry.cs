using ClickHouse.Driver;
using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Query;

/// <summary>
/// The current <see cref="PromotedAttributeColumns"/> snapshot every log query builder reads
/// (ADR-0062). <see cref="Current"/> is a plain field read - never a ClickHouse round trip on
/// the query path; <see cref="PromotedAttributeRefreshWorker"/> keeps it fresh.
/// </summary>
public interface IPromotedAttributeRegistry
{
    PromotedAttributeColumns Current { get; }

    Task RefreshAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Drops <paramref name="columnName"/> from <see cref="Current"/> right away - called
    /// before the <c>DROP COLUMN</c> runs, so this instance stops generating SQL against a
    /// column that's about to disappear.
    /// </summary>
    void Forget(string columnName);
}

/// <summary>
/// Reads promotions back from <c>system.columns</c> for the <c>logs</c> table the query
/// builders actually query (the Distributed table in cluster mode, which carries the same
/// <c>MATERIALIZED</c> column - see <see cref="PromotedAttributeColumns.PromoteStatements"/>).
/// A failed refresh keeps the previous snapshot rather than dropping to empty: a transient
/// ClickHouse blip shouldn't silently slow every attribute filter back down.
/// </summary>
public sealed class PromotedAttributeRegistry(IClickHouseClient client, IOptions<QueryLimitsOptions> queryLimits, ILogger<PromotedAttributeRegistry> logger) : IPromotedAttributeRegistry
{
    private const string ColumnsSql = """
        SELECT name, default_expression
        FROM system.columns
        WHERE database = currentDatabase() AND table = 'logs'
          AND default_kind = 'MATERIALIZED' AND startsWith(name, 'attr_')
        ORDER BY name
        """;

    private volatile PromotedAttributeColumns _current = PromotedAttributeColumns.Empty;

    public PromotedAttributeColumns Current => _current;

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            var columns = new List<(AttributeBag, string, string)>();
            await using var reader = await client.ExecuteReaderAsync(ColumnsSql, null, QuerySafety.ExecutionTimeOnly(queryLimits.Value), cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var name = reader.GetString(0);
                if (PromotedAttributeColumns.TryParseExpression(reader.GetString(1), out var bag, out var key))
                {
                    columns.Add((bag, key, name));
                }
            }

            _current = new PromotedAttributeColumns(columns);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Refreshing promoted attribute columns failed; keeping the previous {Count} column(s).", _current.Count);
        }
    }

    public void Forget(string columnName) =>
        _current = new PromotedAttributeColumns(_current.All.Where(a => a.ColumnName != columnName));
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

    /// <summary>False when <paramref name="columnName"/> isn't a currently promoted column.</summary>
    Task<bool> DemoteAsync(string columnName, CancellationToken cancellationToken);
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
    // Unfinished MATERIALIZE mutations on the storage table - logs single-node, logs_local
    // (on this node) in cluster mode. Matched to a column by name in ListAsync.
    private const string PendingMutationsSql = """
        SELECT command
        FROM system.mutations
        WHERE database = currentDatabase() AND table IN ('logs', 'logs_local') AND is_done = 0
        """;

    public async Task<PromotedAttributesResponse> ListAsync(CancellationToken cancellationToken)
    {
        await registry.RefreshAsync(cancellationToken);
        var pending = new List<string>();
        await using (var reader = await client.ExecuteReaderAsync(PendingMutationsSql, null, QuerySafety.ExecutionTimeOnly(queryLimits.Value), cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                pending.Add(reader.GetString(0));
            }
        }

        var attributes = registry.Current.All
            .OrderBy(a => a.Bag).ThenBy(a => a.Key, StringComparer.Ordinal)
            .Select(a =>
            {
                var index = PromotedAttributeColumns.IndexNameFor(a.ColumnName);
                var backfilling = pending.Any(c =>
                    c.Contains($"MATERIALIZE COLUMN {a.ColumnName}", StringComparison.Ordinal)
                    || c.Contains($"MATERIALIZE INDEX {index}", StringComparison.Ordinal));
                return new PromotedAttributeInfo(a.Bag, a.Key, a.ColumnName, index, backfilling);
            })
            .ToList();
        return new PromotedAttributesResponse(attributes, PromotedAttributeColumns.MaxPromotedAttributes);
    }

    public async Task<PromoteAttributeOutcome> PromoteAsync(PromoteAttributeRequest request, CancellationToken cancellationToken)
    {
        if (!PromotedAttributeColumns.IsValidKey(request.Key) || !Enum.IsDefined(request.Bag))
        {
            return PromoteAttributeOutcome.InvalidKey;
        }

        await registry.RefreshAsync(cancellationToken);
        var current = registry.Current;
        if (current.TryGetColumn(request.Bag, request.Key, out _))
        {
            return PromoteAttributeOutcome.AlreadyPromoted;
        }

        var columnName = PromotedAttributeColumns.ColumnNameFor(request.Bag, request.Key);
        if (current.All.Any(a => a.ColumnName == columnName))
        {
            return PromoteAttributeOutcome.NameConflict;
        }

        if (current.Count >= PromotedAttributeColumns.MaxPromotedAttributes)
        {
            return PromoteAttributeOutcome.LimitReached;
        }

        foreach (var sql in PromotedAttributeColumns.PromoteStatements(request.Bag, request.Key, clusterMode, request.Backfill))
        {
            await client.ExecuteNonQueryAsync(sql, null, null, cancellationToken);
        }

        await registry.RefreshAsync(cancellationToken);
        return PromoteAttributeOutcome.Promoted;
    }

    public async Task<bool> DemoteAsync(string columnName, CancellationToken cancellationToken)
    {
        await registry.RefreshAsync(cancellationToken);
        // Only a name already in the registry ever reaches the DDL text below.
        if (!registry.Current.All.Any(a => a.ColumnName == columnName))
        {
            return false;
        }

        registry.Forget(columnName);
        foreach (var sql in PromotedAttributeColumns.DemoteStatements(columnName, clusterMode))
        {
            await client.ExecuteNonQueryAsync(sql, null, null, cancellationToken);
        }

        await registry.RefreshAsync(cancellationToken);
        return true;
    }
}
