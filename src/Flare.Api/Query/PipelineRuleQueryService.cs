using System.Text.Json;
using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.ADO.Readers;
using ClickHouse.Driver.Utility;
using Flare.Api.Json;
using Flare.Api.Model;

namespace Flare.Api.Query;

public interface IPipelineRuleQueryService
{
    Task<PipelineRule> CreateAsync(PipelineRuleRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<PipelineRule>> ListAsync(CancellationToken cancellationToken);

    Task<PipelineRule?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<PipelineRule?> UpdateAsync(Guid id, PipelineRuleRequest request, CancellationToken cancellationToken);

    /// <summary>Soft-deletes (inserts a tombstone version) - see 0024_pipeline_rules.sql. Returns false if <paramref name="id"/> doesn't exist.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>
/// The ClickHouse seam for pipeline-rule CRUD - mirrors <see cref="AlertQueryService"/>'s
/// role for <c>alert_rules</c>, against <c>pipeline_rules</c> instead. Unlike
/// <see cref="AlertQueryService"/>, this is <c>Flare.Api</c>'s write-side only:
/// <c>Flare.Ingest</c> reads the same table through its own read-only
/// <c>IPipelineRuleStore</c> (a separate, mirrored implementation - the two services don't
/// reference each other's projects, see <c>Flare.Api.Model.LogEventDto</c>'s remarks for
/// why this boundary mirrors rather than shares types).
/// </summary>
public sealed class PipelineRuleQueryService(IClickHouseClient client, TimeProvider timeProvider) : IPipelineRuleQueryService
{
    private const string RuleColumns = "Id, Name, Description, Enabled, ConditionJson, ActionsJson, CreatedAt, UpdatedAt";

    public async Task<PipelineRule> CreateAsync(PipelineRuleRequest request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var rule = new PipelineRule
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description ?? "",
            Enabled = request.Enabled ?? true,
            Condition = request.Condition,
            Actions = request.Actions ?? [],
            CreatedAt = now,
            UpdatedAt = now,
        };

        await InsertRuleVersionAsync(rule, isDeleted: false, cancellationToken);
        return rule;
    }

    public async Task<IReadOnlyList<PipelineRule>> ListAsync(CancellationToken cancellationToken)
    {
        var sql = $"SELECT {RuleColumns} FROM pipeline_rules FINAL WHERE IsDeleted = 0 ORDER BY Name";
        await using var reader = await client.ExecuteReaderAsync(sql, null, SafetyOptions(), cancellationToken);
        return ReadRules(reader);
    }

    public async Task<PipelineRule?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("id", id);
        var sql = $"SELECT {RuleColumns} FROM pipeline_rules FINAL WHERE Id = {{id:UUID}} AND IsDeleted = 0";
        await using var reader = await client.ExecuteReaderAsync(sql, parameters, SafetyOptions(), cancellationToken);
        return reader.Read() ? ReadRule(reader) : null;
    }

    public async Task<PipelineRule?> UpdateAsync(Guid id, PipelineRuleRequest request, CancellationToken cancellationToken)
    {
        var existing = await GetAsync(id, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        var updated = existing with
        {
            Name = request.Name,
            Description = request.Description ?? "",
            Enabled = request.Enabled ?? true,
            Condition = request.Condition,
            Actions = request.Actions ?? [],
            UpdatedAt = timeProvider.GetUtcNow(),
        };

        await InsertRuleVersionAsync(updated, isDeleted: false, cancellationToken);
        return updated;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var existing = await GetAsync(id, cancellationToken);
        if (existing is null)
        {
            return false;
        }

        var tombstone = existing with { UpdatedAt = timeProvider.GetUtcNow() };
        await InsertRuleVersionAsync(tombstone, isDeleted: true, cancellationToken);
        return true;
    }

    private async Task InsertRuleVersionAsync(PipelineRule rule, bool isDeleted, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("id", rule.Id);
        parameters.AddParameter("name", rule.Name);
        parameters.AddParameter("description", rule.Description);
        parameters.AddParameter("enabled", rule.Enabled ? (byte)1 : (byte)0);
        parameters.AddParameter("isDeleted", isDeleted ? (byte)1 : (byte)0);
        parameters.AddParameter("conditionJson", JsonSerializer.Serialize(rule.Condition, PipelineRulesJsonContext.Default.LogFilter));
        parameters.AddParameter("actionsJson", JsonSerializer.Serialize(rule.Actions, PipelineRulesJsonContext.Default.IReadOnlyListPipelineRuleAction));
        parameters.AddParameter("createdAt", rule.CreatedAt.UtcDateTime);
        parameters.AddParameter("updatedAt", rule.UpdatedAt.UtcDateTime);

        const string sql = """
            INSERT INTO pipeline_rules
                (Id, Name, Description, Enabled, IsDeleted, ConditionJson, ActionsJson, CreatedAt, UpdatedAt)
            VALUES
                ({id:UUID}, {name:String}, {description:String}, {enabled:UInt8}, {isDeleted:UInt8}, {conditionJson:String}, {actionsJson:String}, {createdAt:DateTime64(3)}, {updatedAt:DateTime64(3)})
            """;

        await client.ExecuteNonQueryAsync(sql, parameters, SafetyOptions(), cancellationToken);
    }

    private static List<PipelineRule> ReadRules(ClickHouseDataReader reader)
    {
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
        Description = reader.GetString(2),
        Enabled = reader.GetByte(3) != 0,
        Condition = JsonSerializer.Deserialize(reader.GetString(4), PipelineRulesJsonContext.Default.LogFilter) ?? new LogFilter(),
        Actions = JsonSerializer.Deserialize(reader.GetString(5), PipelineRulesJsonContext.Default.IReadOnlyListPipelineRuleAction) ?? [],
        CreatedAt = ReadUtc(reader, 6),
        UpdatedAt = ReadUtc(reader, 7),
    };

    /// <summary>See <see cref="LogQueryService"/>'s identical helper's remarks - same <c>DateTime64</c>/<c>Kind=Unspecified</c> driver behavior applies here.</summary>
    private static DateTimeOffset ReadUtc(ClickHouseDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    /// <summary>Same query-safety rationale as <see cref="LogQueryService.SafetyOptions"/>, used here for rule CRUD.</summary>
    private static QueryOptions SafetyOptions() => new()
    {
        CustomSettings = new Dictionary<string, object>
        {
            ["max_execution_time"] = 30,
            ["timeout_before_checking_execution_speed"] = 0,
            ["max_rows_to_read"] = 1_000_000_000,
            ["max_result_rows"] = 10_000,
            ["result_overflow_mode"] = "break",
        },
    };
}
