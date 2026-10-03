using System.Text.Json;
using System.Text.Json.Serialization;
using ClickHouse.Driver;

namespace Flare.Ingest.Pipeline.MetricRules;

public interface IMetricAttributeRuleStore
{
    Task<IReadOnlyList<MetricAttributeRule>> GetEnabledRulesAsync(CancellationToken cancellationToken);
}

[JsonSerializable(typeof(string[]))]
internal sealed partial class MetricAttributeRuleJsonContext : JsonSerializerContext;

/// <summary>
/// Read-only <see cref="IMetricAttributeRuleStore"/> over <c>metric_attribute_rules</c>:
/// latest version per <c>Id</c> (not <c>FINAL</c> - see <see cref="Rules.ClickHousePipelineRuleStore"/>),
/// enabled and not deleted, oldest first so rules apply in creation order.
/// </summary>
public sealed class ClickHouseMetricAttributeRuleStore(IClickHouseClient client) : IMetricAttributeRuleStore
{
    private const string Sql = """
        SELECT Id, Name, MetricName, Mode, AttributesJson
        FROM (
            SELECT Id, Name, MetricName, Mode, AttributesJson, Enabled, IsDeleted, CreatedAt
            FROM metric_attribute_rules
            ORDER BY UpdatedAt DESC
            LIMIT 1 BY Id
        )
        WHERE IsDeleted = 0 AND Enabled = 1
        ORDER BY CreatedAt
        """;

    public async Task<IReadOnlyList<MetricAttributeRule>> GetEnabledRulesAsync(CancellationToken cancellationToken)
    {
        await using var reader = await client.ExecuteReaderAsync(Sql, null, SafetyOptions(), cancellationToken);
        var rules = new List<MetricAttributeRule>();
        while (reader.Read())
        {
            rules.Add(new MetricAttributeRule
            {
                Id = reader.GetGuid(0),
                Name = reader.GetString(1),
                MetricName = reader.GetString(2),
                Mode = (MetricAttributeRuleMode)reader.GetByte(3),
                Attributes = JsonSerializer.Deserialize(reader.GetString(4), MetricAttributeRuleJsonContext.Default.StringArray) ?? [],
            });
        }

        return rules;
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
