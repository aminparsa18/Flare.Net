using System.Text.Json;
using System.Text.Json.Serialization;
using ClickHouse.Driver;
using Flare.Ingest.Pipeline.Rules;

namespace Flare.Ingest.Pipeline.LogMetrics;

public interface ILogMetricStore
{
    Task<IReadOnlyList<LogMetricDefinition>> GetEnabledAsync(CancellationToken cancellationToken);
}

[JsonSerializable(typeof(string[]))]
internal sealed partial class LogMetricJsonContext : JsonSerializerContext;

/// <summary>
/// Read-only <see cref="ILogMetricStore"/> over <c>log_metrics</c>: latest version per <c>Id</c>
/// (not <c>FINAL</c> - see <see cref="ClickHousePipelineRuleStore"/>), enabled and not deleted.
/// </summary>
public sealed class ClickHouseLogMetricStore(IClickHouseClient client) : ILogMetricStore
{
    private const string Sql = """
        SELECT Id, Name, MetricName, ConditionJson, GroupByJson
        FROM (
            SELECT Id, Name, MetricName, ConditionJson, GroupByJson, Enabled, IsDeleted, CreatedAt
            FROM log_metrics
            ORDER BY UpdatedAt DESC
            LIMIT 1 BY Id
        )
        WHERE IsDeleted = 0 AND Enabled = 1
        ORDER BY CreatedAt
        """;

    public async Task<IReadOnlyList<LogMetricDefinition>> GetEnabledAsync(CancellationToken cancellationToken)
    {
        await using var reader = await client.ExecuteReaderAsync(Sql, null, SafetyOptions(), cancellationToken);
        var definitions = new List<LogMetricDefinition>();
        while (reader.Read())
        {
            definitions.Add(new LogMetricDefinition
            {
                Id = reader.GetGuid(0),
                Name = reader.GetString(1),
                MetricName = reader.GetString(2),
                Condition = JsonSerializer.Deserialize(reader.GetString(3), PipelineRuleJsonContext.Default.PipelineRuleCondition) ?? new PipelineRuleCondition(),
                GroupBy = JsonSerializer.Deserialize(reader.GetString(4), LogMetricJsonContext.Default.StringArray) ?? [],
            });
        }

        return definitions;
    }

    private static QueryOptions SafetyOptions() => new()
    {
        CustomSettings = new Dictionary<string, object>
        {
            ["max_execution_time"] = 10,
            ["timeout_before_checking_execution_speed"] = 0,
            ["max_rows_to_read"] = 1_000_000_000,
            ["max_result_rows"] = 10_000,
            ["result_overflow_mode"] = "break",
        },
    };
}
