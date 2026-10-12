using System.Text.Json;
using System.Threading.Channels;
using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Json;
using Flare.Api.Query;
using Microsoft.Extensions.Options;

namespace Flare.Api.Retention;

public interface IRetentionService
{
    /// <summary>The actual (live from ClickHouse) and expected (last requested) retention of every signal.</summary>
    Task<RetentionResponse> GetAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Records a <c>pending</c> operation per signal and queues the <c>MODIFY TTL</c> work for
    /// the background worker. Throws <see cref="RetentionBusyException"/> while an earlier
    /// request is still applying - mutations aren't queued behind each other.
    /// </summary>
    Task<Guid> RequestAsync(IReadOnlyDictionary<string, int> daysBySignal, IReadOnlyDictionary<string, int>? coldAfterBySignal, IReadOnlyDictionary<string, IReadOnlyList<RetentionRule>>? rulesBySignal, string requestedBy, CancellationToken cancellationToken);
}

/// <summary>Thrown by <see cref="IRetentionService.RequestAsync"/> when cold storage is asked for but ClickHouse has no cold volume.</summary>
public sealed class ColdStorageUnavailableException() : InvalidOperationException("Cold storage isn't configured on this ClickHouse. Start Flare with docker-compose.cold-storage.yml (or mount db/clickhouse/config/cold-storage.xml) first.");

/// <summary>Thrown by <see cref="IRetentionService.RequestAsync"/> when a previous request is still <c>pending</c>.</summary>
public sealed class RetentionBusyException() : InvalidOperationException("A retention change is still being applied. Wait for it to finish before requesting another.");

/// <summary>
/// ClickHouse seam for retention (docs-internal/adr/0143-retention-ttl.md). <c>ALTER ... MODIFY
/// TTL</c> is a metadata change on the table but still a cluster-wide DDL round trip, so
/// <see cref="RequestAsync"/> only records <c>pending</c> rows and returns; this
/// <see cref="BackgroundService"/> applies them and records <c>success</c>/<c>failed</c>.
/// </summary>
public sealed class RetentionService(
    IClickHouseClient client,
    IOptions<QueryLimitsOptions> queryLimits,
    TimeProvider timeProvider,
    ILogger<RetentionService> logger,
    bool clusterMode) : BackgroundService, IRetentionService
{
    /// <summary>A <c>pending</c> row not updated for this long is reported as failed (the API restarted mid-apply) and no longer blocks new requests.</summary>
    internal static readonly TimeSpan PendingStaleAfter = TimeSpan.FromMinutes(30);

    private const int HistoryRows = 200;

    private readonly Channel<Job> _jobs = Channel.CreateUnbounded<Job>();
    private readonly SemaphoreSlim _requestGate = new(1, 1);

    private readonly record struct Job(Guid TransactionId, IReadOnlyDictionary<string, (int Days, int ColdAfterDays, IReadOnlyList<RetentionRule> Rules)> BySignal, string RequestedBy, DateTimeOffset RequestedAt);

    private sealed record Operation(Guid TransactionId, string Signal, int Days, int ColdAfterDays, IReadOnlyList<RetentionRule> Rules, string Status, string Error, DateTimeOffset RequestedAt, DateTimeOffset UpdatedAt);

    public async Task<RetentionResponse> GetAsync(CancellationToken cancellationToken)
    {
        var actual = await ReadActualAsync(cancellationToken);
        var defaults = await ReadRetentionDefaultsAsync(cancellationToken);
        var operations = await ReadOperationsAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();

        var signals = new List<SignalRetention>();
        foreach (var signal in RetentionSignal.All)
        {
            var state = RetentionSql.Combine(signal.Tables.Select(t => actual.GetValueOrDefault(t, new TtlState(TtlKind.None, 0))).ToArray());
            IReadOnlyList<RetentionRule> actualRules = [];
            if (state is { Kind: TtlKind.Days, PerResource: true })
            {
                // The TTL reads each row's column, so the rules and the default retention live in the column's default.
                var expressions = signal.Tables.Select(t => defaults.GetValueOrDefault(t, "")).Distinct().ToArray();
                var parsed = expressions.Length == 1 ? RetentionRuleSql.ParseDefaultExpression(expressions[0]) : null;
                state = parsed is { } p
                    ? state with { Days = p.DefaultDays }
                    : new TtlState(TtlKind.Custom, 0);
                actualRules = parsed?.Rules ?? [];
            }

            var last = operations.FirstOrDefault(o => o.Signal == signal.Name);
            var (status, error) = last is null ? (null, null) : EffectiveStatus(last, now);
            signals.Add(new SignalRetention
            {
                Signal = signal.Name,
                Tables = signal.Tables,
                ActualDays = state.Kind == TtlKind.Days ? state.Days : null,
                ActualColdAfterDays = state.Kind == TtlKind.Days && state.ColdAfterDays > 0 ? state.ColdAfterDays : null,
                ActualState = state.Kind.ToString().ToLowerInvariant(),
                ExpectedDays = last?.Days,
                ExpectedColdAfterDays = last?.ColdAfterDays,
                ActualRules = actualRules,
                ExpectedRules = last?.Rules ?? [],
                Status = status,
                Error = error,
                TransactionId = last?.TransactionId,
                UpdatedAt = last?.UpdatedAt,
            });
        }

        return new RetentionResponse { Signals = signals, ColdStorage = await ReadColdStorageAsync(cancellationToken) };
    }

    public async Task<Guid> RequestAsync(IReadOnlyDictionary<string, int> daysBySignal, IReadOnlyDictionary<string, int>? coldAfterBySignal, IReadOnlyDictionary<string, IReadOnlyList<RetentionRule>>? rulesBySignal, string requestedBy, CancellationToken cancellationToken)
    {
        await _requestGate.WaitAsync(cancellationToken);
        try
        {
            var now = timeProvider.GetUtcNow();
            var operations = await ReadOperationsAsync(cancellationToken);
            if (operations.Any(o => EffectiveStatus(o, now).Status == RetentionStatus.Pending))
            {
                throw new RetentionBusyException();
            }

            // Canonical signal names, so "Logs" and "logs" can't both be recorded.
            var cold = (coldAfterBySignal ?? new Dictionary<string, int>()).ToDictionary(kv => RetentionSignal.Find(kv.Key)!.Name, kv => kv.Value);
            var rules = (rulesBySignal ?? new Dictionary<string, IReadOnlyList<RetentionRule>>()).ToDictionary(kv => RetentionSignal.Find(kv.Key)!.Name, kv => kv.Value);
            var normalized = daysBySignal.ToDictionary(
                kv => RetentionSignal.Find(kv.Key)!.Name,
                kv => (Days: kv.Value, ColdAfterDays: cold.GetValueOrDefault(RetentionSignal.Find(kv.Key)!.Name), Rules: rules.GetValueOrDefault(RetentionSignal.Find(kv.Key)!.Name) ?? []));

            if (normalized.Values.Any(v => v.ColdAfterDays > 0) && !(await ReadColdStorageAsync(cancellationToken)).Available)
            {
                throw new ColdStorageUnavailableException();
            }

            var transactionId = Guid.NewGuid();
            foreach (var (signal, (days, coldAfterDays, signalRules)) in normalized)
            {
                await InsertOperationAsync(transactionId, signal, days, coldAfterDays, signalRules, RetentionStatus.Pending, "", requestedBy, now, now, cancellationToken);
            }

            await _jobs.Writer.WriteAsync(new Job(transactionId, normalized, requestedBy, now), cancellationToken);
            return transactionId;
        }
        finally
        {
            _requestGate.Release();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _jobs.Reader.ReadAllAsync(stoppingToken))
        {
            foreach (var (signalName, (days, coldAfterDays, rules)) in job.BySignal)
            {
                var signal = RetentionSignal.Find(signalName)!;
                try
                {
                    var perResource = rules.Count > 0;
                    var defaultExpression = RetentionRuleSql.BuildDefaultExpression(rules, days);
                    foreach (var table in signal.Tables)
                    {
                        if (coldAfterDays > 0)
                        {
                            await EnsureStoragePolicyAsync(table, stoppingToken);
                        }

                        // Rules on: the column must compute the right value before the TTL starts reading it.
                        // Rules off: point the TTL back at a fixed number first, only then reset the column.
                        if (perResource)
                        {
                            await RunAlterAsync(RetentionSql.BuildSetRetentionDefault(table, defaultExpression, clusterMode), stoppingToken);
                        }

                        await RunAlterAsync([RetentionSql.BuildAlter(table, signal.TimeColumn, days, clusterMode, coldAfterDays, perResource)], stoppingToken);

                        if (!perResource)
                        {
                            await RunAlterAsync(RetentionSql.BuildSetRetentionDefault(table, RetentionRuleSql.BuildDefaultExpression([], 0), clusterMode), stoppingToken);
                        }
                    }

                    logger.LogInformation("Retention for {Signal} set to {Days} days, cold after {ColdAfterDays} (0 = off).", signal.Name, days, coldAfterDays);
                    await InsertOperationAsync(job.TransactionId, signal.Name, days, coldAfterDays, rules, RetentionStatus.Success, "", job.RequestedBy, job.RequestedAt, timeProvider.GetUtcNow(), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Applying retention for {Signal} failed.", signal.Name);
                    await InsertOperationAsync(job.TransactionId, signal.Name, days, coldAfterDays, rules, RetentionStatus.Failed, ex.Message, job.RequestedBy, job.RequestedAt, timeProvider.GetUtcNow(), stoppingToken);
                }
            }
        }
    }

    private async Task RunAlterAsync(IReadOnlyList<string> statements, CancellationToken cancellationToken)
    {
        foreach (var statement in statements)
        {
            await client.ExecuteNonQueryAsync(
                statement,
                null,
                new QueryOptions { CustomSettings = new Dictionary<string, object>(RetentionSql.AlterSettings) },
                cancellationToken);
        }
    }

    private async Task<Dictionary<string, string>> ReadRetentionDefaultsAsync(CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>();
        await using var reader = await client.ExecuteReaderAsync(RetentionSql.BuildRetentionDefaultsSql(clusterMode), null, SafetyOptions(), cancellationToken);
        while (reader.Read())
        {
            var name = reader.GetString(0);
            result[clusterMode ? name[..^"_local".Length] : name] = reader.GetString(1);
        }

        return result;
    }

    /// <summary>A TO VOLUME rule is only accepted once the table's storage policy has that volume; assigning it is idempotent but skipped when already set.</summary>
    private async Task EnsureStoragePolicyAsync(string table, CancellationToken cancellationToken)
    {
        await using (var reader = await client.ExecuteReaderAsync(RetentionSql.BuildCurrentPolicySql(table, clusterMode), null, SafetyOptions(), cancellationToken))
        {
            if (reader.Read() && reader.GetString(0) == RetentionSql.StoragePolicy)
            {
                return;
            }
        }

        await client.ExecuteNonQueryAsync(
            RetentionSql.BuildAssignPolicy(table, clusterMode),
            null,
            new QueryOptions { CustomSettings = new Dictionary<string, object>(RetentionSql.AlterSettings) },
            cancellationToken);
    }

    private async Task<ColdStorageInfo> ReadColdStorageAsync(CancellationToken cancellationToken)
    {
        bool available;
        await using (var reader = await client.ExecuteReaderAsync(RetentionSql.ColdAvailableSql, null, SafetyOptions(), cancellationToken))
        {
            available = reader.Read() && reader.GetFieldValue<ulong>(0) > 0;
        }

        var disks = new List<StorageDisk>();
        await using (var reader = await client.ExecuteReaderAsync(RetentionSql.DisksSql, null, SafetyOptions(), cancellationToken))
        {
            while (reader.Read())
            {
                disks.Add(new StorageDisk
                {
                    Name = reader.GetString(0),
                    Type = reader.GetString(1),
                    FreeBytes = (long)reader.GetFieldValue<ulong>(2),
                    TotalBytes = (long)reader.GetFieldValue<ulong>(3),
                });
            }
        }

        return new ColdStorageInfo { Available = available, Disks = disks };
    }

    private static (string Status, string? Error) EffectiveStatus(Operation operation, DateTimeOffset now) =>
        operation.Status == RetentionStatus.Pending && now - operation.UpdatedAt > PendingStaleAfter
            ? (RetentionStatus.Failed, "Interrupted: the API restarted before this change finished applying. Request it again.")
            : (operation.Status, string.IsNullOrEmpty(operation.Error) ? null : operation.Error);

    private async Task<Dictionary<string, TtlState>> ReadActualAsync(CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, TtlState>();
        await using var reader = await client.ExecuteReaderAsync(RetentionSql.BuildCreateQueriesSql(clusterMode), null, SafetyOptions(), cancellationToken);
        while (reader.Read())
        {
            var name = reader.GetString(0);
            result[clusterMode ? name[..^"_local".Length] : name] = RetentionSql.ParseTtl(reader.GetString(1));
        }

        return result;
    }

    /// <summary>Latest version per (transaction, signal), newest request first. Picked at query time rather than with <c>FINAL</c> for the cluster-sharding reason in <see cref="Query.LatestVersionSql"/>.</summary>
    private async Task<List<Operation>> ReadOperationsAsync(CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT TransactionId, Signal, Days, ColdAfterDays, RulesJson, Status, Error, RequestedAt, UpdatedAt
            FROM (
                SELECT TransactionId, Signal, Days, ColdAfterDays, RulesJson, Status, Error, RequestedAt, UpdatedAt
                FROM retention_operations
                ORDER BY UpdatedAt DESC LIMIT 1 BY TransactionId, Signal)
            ORDER BY RequestedAt DESC, UpdatedAt DESC
            LIMIT {HistoryRows}
            """;
        var operations = new List<Operation>();
        await using var reader = await client.ExecuteReaderAsync(sql, null, SafetyOptions(), cancellationToken);
        while (reader.Read())
        {
            operations.Add(new Operation(
                reader.GetGuid(0),
                reader.GetString(1),
                (int)reader.GetFieldValue<uint>(2),
                (int)reader.GetFieldValue<uint>(3),
                ParseRules(reader.GetString(4)),
                reader.GetString(5),
                reader.GetString(6),
                new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(7), DateTimeKind.Utc)),
                new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(8), DateTimeKind.Utc))));
        }

        return operations;
    }

    private async Task InsertOperationAsync(Guid transactionId, string signal, int days, int coldAfterDays, IReadOnlyList<RetentionRule> rules, string status, string error, string requestedBy, DateTimeOffset requestedAt, DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("transactionId", transactionId);
        parameters.AddParameter("signal", signal);
        parameters.AddParameter("days", (uint)days);
        parameters.AddParameter("coldAfterDays", (uint)coldAfterDays);
        parameters.AddParameter("rulesJson", rules.Count == 0 ? "" : JsonSerializer.Serialize(rules, RetentionJsonContext.Default.IReadOnlyListRetentionRule));
        parameters.AddParameter("status", status);
        parameters.AddParameter("error", error);
        parameters.AddParameter("requestedBy", requestedBy);
        parameters.AddParameter("requestedAt", requestedAt.UtcDateTime);
        parameters.AddParameter("updatedAt", updatedAt.UtcDateTime);

        const string sql = """
            INSERT INTO retention_operations
                (TransactionId, Signal, Days, ColdAfterDays, RulesJson, Status, Error, RequestedBy, RequestedAt, UpdatedAt)
            VALUES
                ({transactionId:UUID}, {signal:String}, {days:UInt32}, {coldAfterDays:UInt32}, {rulesJson:String}, {status:String}, {error:String}, {requestedBy:String}, {requestedAt:DateTime64(3)}, {updatedAt:DateTime64(3)})
            """;

        await client.ExecuteNonQueryAsync(sql, parameters, SafetyOptions(), cancellationToken);
    }

    private static IReadOnlyList<RetentionRule> ParseRules(string json) =>
        string.IsNullOrEmpty(json) ? [] : JsonSerializer.Deserialize(json, RetentionJsonContext.Default.IReadOnlyListRetentionRule) ?? [];

    private QueryOptions SafetyOptions() => QuerySafety.ExecutionTimeOnly(queryLimits.Value);
}
