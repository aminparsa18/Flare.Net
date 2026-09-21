using System.Text.Json;
using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Readers;

namespace Flare.Ingest.Pipeline.Rules;

/// <summary>
/// <see cref="IPipelineRuleStore"/> backed by <c>Aspire.ClickHouse.Driver</c>'s
/// <see cref="IClickHouseClient"/> - the first read use of the client on the
/// <c>Flare.Ingest</c> side, which otherwise only ever writes (see
/// <see cref="ClickHouseLogEventWriter"/>). Same <c>SELECT ... FINAL WHERE IsDeleted = 0</c>
/// query shape <c>Flare.Api</c>'s <c>AlertQueryService.GetEnabledRulesAsync</c> uses against
/// <c>alert_rules</c>, here against <c>pipeline_rules</c> - a separate, mirrored
/// implementation rather than a <c>ProjectReference</c> to <c>Flare.Api</c>, consistent with
/// this boundary never sharing types (see <see cref="PipelineRuleCondition"/>'s remarks).
/// </summary>
public sealed class ClickHousePipelineRuleStore(IClickHouseClient client) : IPipelineRuleStore
{
    private const string Sql = """
        SELECT Id, Name, ConditionJson, ActionsJson
        FROM pipeline_rules
        FINAL
        WHERE IsDeleted = 0 AND Enabled = 1
        ORDER BY CreatedAt
        """;

    public async Task<IReadOnlyList<PipelineRule>> GetEnabledRulesAsync(CancellationToken cancellationToken)
    {
        await using var reader = await client.ExecuteReaderAsync(Sql, null, SafetyOptions(), cancellationToken);
        var rules = new List<PipelineRule>();
        while (reader.Read())
        {
            rules.Add(ReadRule(reader));
        }

        return rules;
    }

    private static PipelineRule ReadRule(ClickHouseDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        Name = reader.GetString(1),
        Condition = JsonSerializer.Deserialize(reader.GetString(2), PipelineRuleJsonContext.Default.PipelineRuleCondition) ?? new PipelineRuleCondition(),
        Actions = JsonSerializer.Deserialize(reader.GetString(3), PipelineRuleJsonContext.Default.IReadOnlyListPipelineRuleAction) ?? [],
    };

    /// <summary>
    /// Tighter cap than a dashboard-facing query, same reasoning
    /// <c>AlertQueryService.EvaluationSafetyOptions</c> gives for evaluation queries run
    /// once per poll tick - a runaway query here blocks <see cref="PipelineRuleCache"/>'s
    /// whole refresh, not just one dashboard request. Row count is expected to be tens to
    /// low hundreds, same as <c>alert_rules</c>.
    /// </summary>
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
