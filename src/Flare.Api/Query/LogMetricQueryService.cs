using System.Text.Json;
using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.ADO.Readers;
using ClickHouse.Driver.Utility;
using Flare.Api.Json;
using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Query;

public interface ILogMetricQueryService
{
    Task<LogMetric> CreateAsync(LogMetricRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<LogMetric>> ListAsync(CancellationToken cancellationToken);

    Task<LogMetric?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<LogMetric?> UpdateAsync(Guid id, LogMetricRequest request, CancellationToken cancellationToken);

    /// <summary>Soft-deletes (inserts a tombstone version) - see 0064_log_metrics.sql. Returns false if <paramref name="id"/> doesn't exist. Already-emitted data stays in <c>metrics_sum</c>.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>ClickHouse seam for log-metric CRUD against <c>log_metrics</c> - same tombstone/latest-version shape as <see cref="MetricAttributeRuleQueryService"/>. <c>Flare.Ingest</c> reads the table through its own mirrored read-only store.</summary>
public sealed class LogMetricQueryService(IClickHouseClient client, IOptions<QueryLimitsOptions> queryLimits, TimeProvider timeProvider) : ILogMetricQueryService
{
    private const string Columns = "Id, Name, Description, Enabled, MetricName, ConditionJson, GroupByJson, CreatedAt, UpdatedAt";

    public async Task<LogMetric> CreateAsync(LogMetricRequest request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var metric = new LogMetric
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Description = request.Description ?? "",
            Enabled = request.Enabled ?? true,
            MetricName = request.MetricName.Trim(),
            Condition = request.Condition ?? new LogFilter(),
            GroupBy = Normalize(request.GroupBy),
            CreatedAt = now,
            UpdatedAt = now,
        };

        await InsertVersionAsync(metric, isDeleted: false, cancellationToken);
        return metric;
    }

    public async Task<IReadOnlyList<LogMetric>> ListAsync(CancellationToken cancellationToken)
    {
        var sql = LatestVersionSql.Select("log_metrics", Columns, orderBy: "Name");
        await using var reader = await client.ExecuteReaderAsync(sql, null, SafetyOptions(), cancellationToken);
        var metrics = new List<LogMetric>();
        while (reader.Read())
        {
            metrics.Add(Read(reader));
        }

        return metrics;
    }

    public async Task<LogMetric?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("id", id);
        var sql = LatestVersionSql.Select("log_metrics", Columns, idWhere: "Id = {id:UUID}");
        await using var reader = await client.ExecuteReaderAsync(sql, parameters, SafetyOptions(), cancellationToken);
        return reader.Read() ? Read(reader) : null;
    }

    public async Task<LogMetric?> UpdateAsync(Guid id, LogMetricRequest request, CancellationToken cancellationToken)
    {
        var existing = await GetAsync(id, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        var updated = existing with
        {
            Name = request.Name.Trim(),
            Description = request.Description ?? "",
            Enabled = request.Enabled ?? true,
            MetricName = request.MetricName.Trim(),
            Condition = request.Condition ?? new LogFilter(),
            GroupBy = Normalize(request.GroupBy),
            UpdatedAt = timeProvider.GetUtcNow(),
        };

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

    private static string[] Normalize(IReadOnlyList<string>? keys) =>
        (keys ?? []).Select(k => k.Trim()).Distinct(StringComparer.Ordinal).ToArray();

    private async Task InsertVersionAsync(LogMetric metric, bool isDeleted, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("id", metric.Id);
        parameters.AddParameter("name", metric.Name);
        parameters.AddParameter("description", metric.Description);
        parameters.AddParameter("enabled", metric.Enabled ? (byte)1 : (byte)0);
        parameters.AddParameter("isDeleted", isDeleted ? (byte)1 : (byte)0);
        parameters.AddParameter("metricName", metric.MetricName);
        parameters.AddParameter("conditionJson", JsonSerializer.Serialize(metric.Condition, LogMetricsJsonContext.Default.LogFilter));
        parameters.AddParameter("groupByJson", JsonSerializer.Serialize(metric.GroupBy, LogMetricsJsonContext.Default.IReadOnlyListString));
        parameters.AddParameter("createdAt", metric.CreatedAt.UtcDateTime);
        parameters.AddParameter("updatedAt", metric.UpdatedAt.UtcDateTime);

        const string sql = """
            INSERT INTO log_metrics
                (Id, Name, Description, Enabled, IsDeleted, MetricName, ConditionJson, GroupByJson, CreatedAt, UpdatedAt)
            VALUES
                ({id:UUID}, {name:String}, {description:String}, {enabled:UInt8}, {isDeleted:UInt8}, {metricName:String}, {conditionJson:String}, {groupByJson:String}, {createdAt:DateTime64(3)}, {updatedAt:DateTime64(3)})
            """;

        await client.ExecuteNonQueryAsync(sql, parameters, SafetyOptions(), cancellationToken);
    }

    private static LogMetric Read(ClickHouseDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        Name = reader.GetString(1),
        Description = reader.GetString(2),
        Enabled = reader.GetByte(3) != 0,
        MetricName = reader.GetString(4),
        Condition = JsonSerializer.Deserialize(reader.GetString(5), LogMetricsJsonContext.Default.LogFilter) ?? new LogFilter(),
        GroupBy = JsonSerializer.Deserialize(reader.GetString(6), LogMetricsJsonContext.Default.IReadOnlyListString) ?? [],
        CreatedAt = ReadUtc(reader, 7),
        UpdatedAt = ReadUtc(reader, 8),
    };

    /// <summary>See <see cref="LogQueryService"/>'s identical helper - same <c>DateTime64</c>/<c>Kind=Unspecified</c> driver behavior.</summary>
    private static DateTimeOffset ReadUtc(ClickHouseDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    private QueryOptions SafetyOptions() => QuerySafety.Full(queryLimits.Value);
}
