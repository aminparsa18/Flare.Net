using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Json;
using Flare.Api.Query;
using Microsoft.Extensions.Options;

namespace Flare.Api.Erasure;

public interface IErasureService
{
    /// <summary>Dry run: how many rows of each table belong to the subject.</summary>
    Task<ErasurePreviewResponse> PreviewAsync(IReadOnlyList<ErasureIdentifier> identifiers, CancellationToken cancellationToken);

    /// <summary>
    /// Counts the subject's rows, records a <c>pending</c> operation and queues the <c>ALTER ... DELETE</c> mutations for the
    /// background worker. Throws <see cref="ErasureBusyException"/> while an earlier request is still being queued.
    /// </summary>
    Task<Guid> StartAsync(IReadOnlyList<ErasureIdentifier> identifiers, string requestedBy, CancellationToken cancellationToken);

    /// <summary>Recent erasure requests, newest first, each with live mutation progress.</summary>
    Task<IReadOnlyList<ErasureOperation>> ListAsync(CancellationToken cancellationToken);

    Task<ErasureOperation?> GetAsync(Guid transactionId, CancellationToken cancellationToken);

    /// <summary>One JSON line per matching row (<c>{"table":"logs","row":{...}}</c>), capped per table; a capped table ends with <c>{"table":"logs","truncated":true}</c>.</summary>
    IAsyncEnumerable<string> ExportAsync(IReadOnlyList<ErasureIdentifier> identifiers, CancellationToken cancellationToken);
}

/// <summary>Thrown by <see cref="IErasureService.StartAsync"/> when a previous request has not finished queuing its mutations.</summary>
public sealed class ErasureBusyException() : InvalidOperationException("An erasure request is still being submitted. Wait for it to finish before requesting another.");

/// <summary>
/// ClickHouse seam for data-subject erasure and export (docs-internal/adr/0198-data-subject-erasure-export.md). An
/// <c>ALTER TABLE ... DELETE</c> is an asynchronous mutation that rewrites data parts, and in cluster mode a distributed DDL
/// round trip, so <see cref="StartAsync"/> only records a <c>pending</c> row; this <see cref="BackgroundService"/> submits
/// the mutations. It does not wait for them: progress is read from <c>system.mutations</c> when asked.
/// </summary>
public sealed class ErasureService(
    IClickHouseClient client,
    IOptions<QueryLimitsOptions> queryLimits,
    TimeProvider timeProvider,
    ILogger<ErasureService> logger,
    bool clusterMode) : BackgroundService, IErasureService
{
    /// <summary>A <c>pending</c> row not updated for this long is reported as failed (the API restarted before submitting) and no longer blocks new requests.</summary>
    internal static readonly TimeSpan PendingStaleAfter = TimeSpan.FromMinutes(30);

    /// <summary>A submitted table whose mutation is no longer listed in <c>system.mutations</c> counts as finished only after this long; right after submission it may simply not be visible yet.</summary>
    internal static readonly TimeSpan MutationVisibilityGrace = TimeSpan.FromMinutes(2);

    internal const int ExportRowLimitPerTable = 100_000;

    private const int HistoryRows = 20;

    private readonly Channel<Job> _jobs = Channel.CreateUnbounded<Job>();
    private readonly SemaphoreSlim _requestGate = new(1, 1);

    private readonly record struct Job(Guid TransactionId, IReadOnlyList<ErasureIdentifier> Identifiers, IReadOnlyList<string> SessionIds, IReadOnlyDictionary<string, long> Counts, string RequestedBy, DateTimeOffset RequestedAt);

    private sealed record Stored(Guid TransactionId, string Status, string Error, string[] IdentifierKeys, string SubjectHash, IReadOnlyDictionary<string, long> Counts, string RequestedBy, DateTimeOffset RequestedAt, DateTimeOffset UpdatedAt);

    public async Task<ErasurePreviewResponse> PreviewAsync(IReadOnlyList<ErasureIdentifier> identifiers, CancellationToken cancellationToken)
    {
        var (sessions, truncated) = await FindSessionsAsync(identifiers, cancellationToken);
        var counts = await CountAsync(identifiers, sessions, cancellationToken);
        return new ErasurePreviewResponse
        {
            Tables = counts.Select(kv => new ErasureTableCount { Table = kv.Key, Rows = kv.Value }).ToList(),
            TotalRows = counts.Values.Sum(),
            SessionCount = sessions.Count,
            SessionsTruncated = truncated,
        };
    }

    public async Task<Guid> StartAsync(IReadOnlyList<ErasureIdentifier> identifiers, string requestedBy, CancellationToken cancellationToken)
    {
        await _requestGate.WaitAsync(cancellationToken);
        try
        {
            var now = timeProvider.GetUtcNow();
            if ((await ReadStoredAsync(null, cancellationToken)).Any(o => o.Status == ErasureOperationStatus.Pending && now - o.UpdatedAt <= PendingStaleAfter))
            {
                throw new ErasureBusyException();
            }

            // Sessions are resolved before anything is deleted: the spans that name them are among the rows going away.
            var (sessions, _) = await FindSessionsAsync(identifiers, cancellationToken);
            var counts = await CountAsync(identifiers, sessions, cancellationToken);

            var transactionId = Guid.NewGuid();
            await InsertOperationAsync(transactionId, ErasureOperationStatus.Pending, "", identifiers, counts, requestedBy, now, now, cancellationToken);
            await _jobs.Writer.WriteAsync(new Job(transactionId, identifiers, sessions, counts, requestedBy, now), cancellationToken);
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
            var keys = job.Identifiers.Select(i => i.Key).Distinct().ToArray();
            try
            {
                foreach (var table in ErasureSql.Tables.Where(t => job.Counts.GetValueOrDefault(t.Name) > 0))
                {
                    await client.ExecuteNonQueryAsync(
                        ErasureSql.BuildDelete(table, job.Identifiers, job.SessionIds, job.TransactionId, clusterMode),
                        null,
                        new QueryOptions { CustomSettings = new Dictionary<string, object> { ["mutations_sync"] = 0 } },
                        stoppingToken);
                }

                logger.LogInformation("Erasure {TransactionId} submitted for subject {SubjectHash} ({Rows} rows).", job.TransactionId, ErasureSql.SubjectHash(job.Identifiers), job.Counts.Values.Sum());
                await InsertOperationAsync(job.TransactionId, ErasureOperationStatus.Submitted, "", job.Identifiers, job.Counts, job.RequestedBy, job.RequestedAt, timeProvider.GetUtcNow(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Submitting erasure {TransactionId} failed.", job.TransactionId);
                await InsertOperationAsync(job.TransactionId, ErasureOperationStatus.Failed, ex.Message, job.Identifiers, job.Counts, job.RequestedBy, job.RequestedAt, timeProvider.GetUtcNow(), stoppingToken);
            }
        }
    }

    public async Task<IReadOnlyList<ErasureOperation>> ListAsync(CancellationToken cancellationToken)
    {
        var result = new List<ErasureOperation>();
        foreach (var stored in await ReadStoredAsync(null, cancellationToken))
        {
            result.Add(await ToOperationAsync(stored, cancellationToken));
        }

        return result;
    }

    public async Task<ErasureOperation?> GetAsync(Guid transactionId, CancellationToken cancellationToken) =>
        (await ReadStoredAsync(transactionId, cancellationToken)).FirstOrDefault() is { } stored
            ? await ToOperationAsync(stored, cancellationToken)
            : null;

    public async IAsyncEnumerable<string> ExportAsync(IReadOnlyList<ErasureIdentifier> identifiers, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var (sessions, _) = await FindSessionsAsync(identifiers, cancellationToken);
        foreach (var table in ErasureSql.Tables)
        {
            var count = 0;
            await using var reader = await client.ExecuteReaderAsync(ErasureSql.BuildExportSql(table, identifiers, sessions, ExportRowLimitPerTable + 1), null, SafetyOptions(), cancellationToken);
            while (reader.Read())
            {
                if (++count > ExportRowLimitPerTable)
                {
                    yield return $$"""{"table":"{{table.Name}}","truncated":true}""";
                    break;
                }

                yield return $$"""{"table":"{{table.Name}}","row":{{reader.GetString(0).TrimEnd('\n')}}}""";
            }
        }
    }

    /// <summary>Stored status plus live mutation progress, folded into one overall status.</summary>
    private async Task<ErasureOperation> ToOperationAsync(Stored stored, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var status = stored.Status;
        var error = string.IsNullOrEmpty(stored.Error) ? null : stored.Error;
        if (status == ErasureOperationStatus.Pending && now - stored.UpdatedAt > PendingStaleAfter)
        {
            (status, error) = (ErasureOperationStatus.Failed, "Interrupted: the API restarted before this request was submitted. Request it again.");
        }

        var mutations = status == ErasureOperationStatus.Submitted ? await ReadMutationsAsync(stored.TransactionId, cancellationToken) : [];
        var tables = new List<ErasureTableProgress>();
        foreach (var table in ErasureSql.Tables)
        {
            var rows = stored.Counts.GetValueOrDefault(table.Name);
            if (rows == 0)
            {
                continue;
            }

            tables.Add(ProgressOf(table.Name, rows, status, mutations.GetValueOrDefault(table.Name), now - stored.UpdatedAt));
        }

        var overall = status switch
        {
            ErasureOperationStatus.Pending => ErasureStatus.Pending,
            ErasureOperationStatus.Failed => ErasureStatus.Failed,
            _ when tables.Any(t => t.Status == ErasureStatus.Failed) => ErasureStatus.Failed,
            _ when tables.All(t => t.Status == ErasureStatus.Success) => ErasureStatus.Success,
            _ => ErasureStatus.Running,
        };

        return new ErasureOperation
        {
            TransactionId = stored.TransactionId,
            Status = overall,
            Error = error ?? tables.FirstOrDefault(t => t.Error is not null)?.Error,
            IdentifierKeys = stored.IdentifierKeys,
            SubjectHash = stored.SubjectHash,
            RequestedBy = stored.RequestedBy,
            RequestedAt = stored.RequestedAt,
            UpdatedAt = stored.UpdatedAt,
            Tables = tables,
        };
    }

    internal static ErasureTableProgress ProgressOf(string table, long rows, string storedStatus, MutationState? mutation, TimeSpan sinceSubmitted)
    {
        if (storedStatus != ErasureOperationStatus.Submitted)
        {
            return new ErasureTableProgress { Table = table, Rows = rows, Status = storedStatus == ErasureOperationStatus.Failed ? ErasureStatus.Failed : ErasureStatus.Pending };
        }

        if (mutation is null)
        {
            // Finished mutations are eventually dropped from system.mutations; right after submission it may just not be listed yet.
            return new ErasureTableProgress { Table = table, Rows = rows, Status = sinceSubmitted > MutationVisibilityGrace ? ErasureStatus.Success : ErasureStatus.Running };
        }

        if (mutation.FailReason is { Length: > 0 } reason)
        {
            return new ErasureTableProgress { Table = table, Rows = rows, Status = ErasureStatus.Failed, PartsToDo = mutation.PartsToDo, Error = reason };
        }

        return new ErasureTableProgress
        {
            Table = table,
            Rows = rows,
            Status = mutation.Done == mutation.Total ? ErasureStatus.Success : ErasureStatus.Running,
            PartsToDo = mutation.PartsToDo,
        };
    }

    internal sealed record MutationState(long Total, long Done, long PartsToDo, string FailReason);

    private async Task<Dictionary<string, MutationState>> ReadMutationsAsync(Guid transactionId, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, MutationState>();
        await using var reader = await client.ExecuteReaderAsync(ErasureSql.BuildMutationProgressSql(transactionId, clusterMode), null, SafetyOptions(), cancellationToken);
        while (reader.Read())
        {
            var name = reader.GetString(0);
            result[clusterMode && name.EndsWith("_local", StringComparison.Ordinal) ? name[..^"_local".Length] : name] = new MutationState(
                // count() is UInt64 but sum(parts_to_do) is Int64; Convert reads either.
                Convert.ToInt64(reader.GetValue(1)),
                Convert.ToInt64(reader.GetValue(2)),
                Convert.ToInt64(reader.GetValue(3)),
                reader.GetString(4));
        }

        return result;
    }

    private async Task<(List<string> Sessions, bool Truncated)> FindSessionsAsync(IReadOnlyList<ErasureIdentifier> identifiers, CancellationToken cancellationToken)
    {
        var sessions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var table in ErasureSql.Tables.Where(t => t.Name is "logs" or "spans"))
        {
            await using var reader = await client.ExecuteReaderAsync(ErasureSql.BuildSessionIdsSql(table, identifiers), null, SafetyOptions(), cancellationToken);
            while (reader.Read())
            {
                sessions.Add(reader.GetString(0));
            }
        }

        var truncated = sessions.Count > ErasureSql.MaxSessions;
        return (sessions.Order(StringComparer.Ordinal).Take(ErasureSql.MaxSessions).ToList(), truncated);
    }

    private async Task<Dictionary<string, long>> CountAsync(IReadOnlyList<ErasureIdentifier> identifiers, IReadOnlyList<string> sessions, CancellationToken cancellationToken)
    {
        var counts = new Dictionary<string, long>();
        foreach (var table in ErasureSql.Tables)
        {
            await using var reader = await client.ExecuteReaderAsync(ErasureSql.BuildCountSql(table, identifiers, sessions), null, SafetyOptions(), cancellationToken);
            counts[table.Name] = reader.Read() ? (long)reader.GetFieldValue<ulong>(0) : 0;
        }

        return counts;
    }

    /// <summary>Latest version per transaction, newest request first. <c>LIMIT 1 BY</c> rather than <c>FINAL</c>, see <see cref="LatestVersionSql"/>.</summary>
    private async Task<List<Stored>> ReadStoredAsync(Guid? transactionId, CancellationToken cancellationToken)
    {
        var where = transactionId is { } id ? $"WHERE TransactionId = '{id}'" : "";
        var sql = $"""
            SELECT TransactionId, Status, Error, IdentifierKeys, SubjectHash, CountsJson, RequestedBy, RequestedAt, UpdatedAt
            FROM (
                SELECT TransactionId, Status, Error, IdentifierKeys, SubjectHash, CountsJson, RequestedBy, RequestedAt, UpdatedAt
                FROM erasure_operations
                {where}
                ORDER BY UpdatedAt DESC LIMIT 1 BY TransactionId)
            ORDER BY RequestedAt DESC
            LIMIT {HistoryRows}
            """;
        var result = new List<Stored>();
        await using var reader = await client.ExecuteReaderAsync(sql, null, SafetyOptions(), cancellationToken);
        while (reader.Read())
        {
            var counts = JsonSerializer.Deserialize(reader.GetString(5), ErasureJsonContext.Default.DictionaryStringInt64) ?? [];
            result.Add(new Stored(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetFieldValue<string[]>(3),
                reader.GetString(4),
                counts,
                reader.GetString(6),
                new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(7), DateTimeKind.Utc)),
                new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(8), DateTimeKind.Utc))));
        }

        return result;
    }

    private async Task InsertOperationAsync(Guid transactionId, string status, string error, IReadOnlyList<ErasureIdentifier> identifiers, IReadOnlyDictionary<string, long> counts, string requestedBy, DateTimeOffset requestedAt, DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("transactionId", transactionId);
        parameters.AddParameter("status", status);
        parameters.AddParameter("error", error);
        parameters.AddParameter("subjectHash", ErasureSql.SubjectHash(identifiers));
        parameters.AddParameter("countsJson", JsonSerializer.Serialize(counts, ErasureJsonContext.Default.IReadOnlyDictionaryStringInt64));
        parameters.AddParameter("requestedBy", requestedBy);
        parameters.AddParameter("requestedAt", requestedAt.UtcDateTime);
        parameters.AddParameter("updatedAt", updatedAt.UtcDateTime);

        // Keys only, never values (migration 0084); rendered as a literal so no array-parameter binding is needed.
        var keys = string.Join(", ", identifiers.Select(i => i.Key).Distinct().Select(ErasureSql.Literal));
        const string template = """
            INSERT INTO erasure_operations
                (TransactionId, Status, Error, IdentifierKeys, SubjectHash, CountsJson, RequestedBy, RequestedAt, UpdatedAt)
            VALUES
                ({transactionId:UUID}, {status:String}, {error:String}, [__KEYS__], {subjectHash:String}, {countsJson:String}, {requestedBy:String}, {requestedAt:DateTime64(3)}, {updatedAt:DateTime64(3)})
            """;
        var sql = template.Replace("__KEYS__", keys);

        await client.ExecuteNonQueryAsync(sql, parameters, SafetyOptions(), cancellationToken);
    }

    private QueryOptions SafetyOptions() => QuerySafety.ExecutionTimeOnly(queryLimits.Value);
}
