using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.ADO.Readers;
using ClickHouse.Driver.Utility;
using Flare.Api.Auth;
using Flare.Api.Model;
using Flare.Api.Slos;
using Microsoft.Extensions.Options;

namespace Flare.Api.Query;

public interface ISloQueryService
{
    Task<Slo> CreateAsync(SloRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<Slo>> ListAsync(CancellationToken cancellationToken);

    Task<Slo?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<Slo?> UpdateAsync(Guid id, SloRequest request, CancellationToken cancellationToken);

    /// <summary>Soft-deletes (inserts a tombstone version) - see 0049_slos.sql. Returns false if <paramref name="id"/> doesn't exist.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Total and bad events of <paramref name="slo"/> over each trailing window, in order - one scan. Also what burn-rate alert rules evaluate.</summary>
    Task<IReadOnlyList<SloWindowStats>> GetWindowStatsAsync(Slo slo, IReadOnlyList<int> windowSeconds, CancellationToken cancellationToken);

    Task<SloStatus> GetStatusAsync(Slo slo, CancellationToken cancellationToken);
}

/// <summary>
/// The ClickHouse seam for SLO CRUD (same <c>ReplacingMergeTree(UpdatedAt)</c> / tombstone-delete /
/// <see cref="LatestVersionSql"/> pattern as <see cref="MaintenanceWindowQueryService"/>) and for
/// the <c>span_sli_minute</c> reads - SQL itself lives in <see cref="SloQueryBuilder"/>.
/// </summary>
public sealed class SloQueryService(IClickHouseClient client, IOptions<QueryLimitsOptions> queryLimits, TimeProvider timeProvider) : ISloQueryService
{
    private const string SloColumns =
        "Id, Name, Description, Kind, ServiceName, OperationName, TargetPercent, LatencyThresholdMs, WindowDays, CreatedAt, UpdatedAt, ProjectId";

    internal static Slo Apply(Slo slo, SloRequest request) => slo with
    {
        Name = request.Name.Trim(),
        Description = request.Description ?? "",
        Kind = request.Kind,
        ServiceName = request.ServiceName.Trim(),
        OperationName = request.OperationName?.Trim() ?? "",
        TargetPercent = request.TargetPercent,
        // Normalized so a stored SLO never carries a threshold its kind ignores.
        LatencyThresholdMs = request.Kind == SloKind.Latency ? request.LatencyThresholdMs ?? 0 : 0,
        WindowDays = request.WindowDays ?? 28,
        ProjectId = ProjectGuard.ResolveForUpdate(slo.ProjectId, request.ProjectId),
    };

    public async Task<Slo> CreateAsync(SloRequest request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var slo = Apply(
            new Slo { Id = Guid.NewGuid(), Name = "", ServiceName = "", TargetPercent = 0, WindowDays = 0, CreatedAt = now, UpdatedAt = now },
            request);
        await InsertVersionAsync(slo, isDeleted: false, cancellationToken);
        return slo;
    }

    public async Task<IReadOnlyList<Slo>> ListAsync(CancellationToken cancellationToken)
    {
        var sql = LatestVersionSql.Select("slos", SloColumns, orderBy: "Name");
        await using var reader = await client.ExecuteReaderAsync(sql, null, SafetyOptions(), cancellationToken);
        var slos = new List<Slo>();
        while (reader.Read())
        {
            slos.Add(ReadSlo(reader));
        }

        return slos;
    }

    public async Task<Slo?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("id", id);
        var sql = LatestVersionSql.Select("slos", SloColumns, idWhere: "Id = {id:UUID}");
        await using var reader = await client.ExecuteReaderAsync(sql, parameters, SafetyOptions(), cancellationToken);
        return reader.Read() ? ReadSlo(reader) : null;
    }

    public async Task<Slo?> UpdateAsync(Guid id, SloRequest request, CancellationToken cancellationToken)
    {
        var existing = await GetAsync(id, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        var updated = Apply(existing, request) with { UpdatedAt = timeProvider.GetUtcNow() };
        await InsertVersionAsync(updated, isDeleted: false, cancellationToken);
        return updated;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var existing = await GetAsync(id, cancellationToken);
        if (existing is null)
        {
            return false;
        }

        await InsertVersionAsync(existing with { UpdatedAt = timeProvider.GetUtcNow() }, isDeleted: true, cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<SloWindowStats>> GetWindowStatsAsync(Slo slo, IReadOnlyList<int> windowSeconds, CancellationToken cancellationToken)
    {
        var query = SloQueryBuilder.BuildCounts(slo, windowSeconds, timeProvider.GetUtcNow());
        await using var reader = await client.ExecuteReaderAsync(query.Sql, query.Parameters, SafetyOptions(), cancellationToken);
        var stats = new List<SloWindowStats>(windowSeconds.Count);
        reader.Read();
        for (var i = 0; i < windowSeconds.Count; i++)
        {
            // An aggregate with no matching rows still returns one row of zeros.
            var total = ReadCount(reader, i * 2);
            var bad = ReadCount(reader, (i * 2) + 1);
            stats.Add(new SloWindowStats
            {
                WindowSeconds = windowSeconds[i],
                Total = total,
                Bad = bad,
                BurnRate = SloCalculator.BurnRate(total, bad, slo.TargetPercent),
            });
        }

        return stats;
    }

    public async Task<SloStatus> GetStatusAsync(Slo slo, CancellationToken cancellationToken)
    {
        var windows = new List<int> { slo.WindowDays * 86400 };
        windows.AddRange(SloQueryBuilder.StatusWindowSeconds);
        var stats = await GetWindowStatsAsync(slo, windows, cancellationToken);

        var series = new List<SloSeriesPoint>();
        var seriesQuery = SloQueryBuilder.BuildSeries(slo, timeProvider.GetUtcNow());
        await using (var reader = await client.ExecuteReaderAsync(seriesQuery.Sql, seriesQuery.Parameters, SafetyOptions(), cancellationToken))
        {
            while (reader.Read())
            {
                series.Add(new SloSeriesPoint
                {
                    TimeUnixMs = new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc)).ToUnixTimeMilliseconds(),
                    Total = ReadCount(reader, 1),
                    Bad = ReadCount(reader, 2),
                });
            }
        }

        var whole = stats[0];
        return new SloStatus
        {
            Slo = slo,
            Total = whole.Total,
            Bad = whole.Bad,
            Sli = SloCalculator.Sli(whole.Total, whole.Bad),
            ErrorBudgetRemaining = SloCalculator.BudgetRemaining(whole.Total, whole.Bad, slo.TargetPercent),
            BurnRates = stats.Skip(1).ToList(),
            Series = series,
        };
    }

    private async Task InsertVersionAsync(Slo slo, bool isDeleted, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("id", slo.Id);
        parameters.AddParameter("name", slo.Name);
        parameters.AddParameter("description", slo.Description);
        parameters.AddParameter("isDeleted", isDeleted ? (byte)1 : (byte)0);
        parameters.AddParameter("kind", slo.Kind.ToString());
        parameters.AddParameter("serviceName", slo.ServiceName);
        parameters.AddParameter("operationName", slo.OperationName);
        parameters.AddParameter("targetPercent", slo.TargetPercent);
        parameters.AddParameter("latencyThresholdMs", (uint)slo.LatencyThresholdMs);
        parameters.AddParameter("windowDays", (ushort)slo.WindowDays);
        parameters.AddParameter("createdAt", slo.CreatedAt.UtcDateTime);
        parameters.AddParameter("updatedAt", slo.UpdatedAt.UtcDateTime);
        parameters.AddParameter("projectId", (object?)slo.ProjectId ?? DBNull.Value);

        const string sql = """
            INSERT INTO slos
                (Id, Name, Description, IsDeleted, Kind, ServiceName, OperationName, TargetPercent, LatencyThresholdMs, WindowDays, CreatedAt, UpdatedAt, ProjectId)
            VALUES
                ({id:UUID}, {name:String}, {description:String}, {isDeleted:UInt8}, {kind:String}, {serviceName:String}, {operationName:String}, {targetPercent:Float64}, {latencyThresholdMs:UInt32}, {windowDays:UInt16}, {createdAt:DateTime64(3)}, {updatedAt:DateTime64(3)}, {projectId:Nullable(UUID)})
            """;

        await client.ExecuteNonQueryAsync(sql, parameters, SafetyOptions(), cancellationToken);
    }

    /// <summary>
    /// A count column read as <see cref="long"/> whatever integer type ClickHouse gave it: a plain
    /// <c>sum</c> is UInt64, but <c>sumIf(TotalCount - Under250ms, ...)</c> comes back Int64.
    /// </summary>
    private static long ReadCount(ClickHouseDataReader reader, int ordinal) => Convert.ToInt64(reader.GetValue(ordinal));

    private static Slo ReadSlo(ClickHouseDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        Name = reader.GetString(1),
        Description = reader.GetString(2),
        Kind = Enum.Parse<SloKind>(reader.GetString(3)),
        ServiceName = reader.GetString(4),
        OperationName = reader.GetString(5),
        TargetPercent = reader.GetDouble(6),
        LatencyThresholdMs = (int)reader.GetFieldValue<uint>(7),
        WindowDays = reader.GetFieldValue<ushort>(8),
        CreatedAt = ReadUtc(reader, 9),
        UpdatedAt = ReadUtc(reader, 10),
        ProjectId = reader.IsDBNull(11) ? null : reader.GetGuid(11),
    };

    /// <summary>See <see cref="MaintenanceWindowQueryService"/>'s identical helper's remarks.</summary>
    private static DateTimeOffset ReadUtc(ClickHouseDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    private QueryOptions SafetyOptions() => QuerySafety.Full(queryLimits.Value);
}
