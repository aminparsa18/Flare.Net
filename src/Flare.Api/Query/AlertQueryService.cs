using System.Text.Json;
using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.ADO.Readers;
using ClickHouse.Driver.Utility;
using Flare.Api.Json;
using Flare.Api.Model;

namespace Flare.Api.Query;

public interface IAlertQueryService
{
    Task<AlertRule> CreateAsync(AlertRuleRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<AlertRule>> ListAsync(CancellationToken cancellationToken);

    Task<AlertRule?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<AlertRule?> UpdateAsync(Guid id, AlertRuleRequest request, CancellationToken cancellationToken);

    /// <summary>Soft-deletes (inserts a tombstone version) - see 0003_alert_rules.sql. Returns false if <paramref name="id"/> doesn't exist.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Read by <c>AlertEvaluationWorker</c> on every poll tick.</summary>
    Task<IReadOnlyList<AlertRule>> GetEnabledRulesAsync(CancellationToken cancellationToken);

    /// <summary>Reuses <see cref="LogFilterSqlBuilder"/> against the <c>logs</c> table with <paramref name="condition"/>'s own From/To overridden by the caller's window.</summary>
    Task<ulong> CountMatchingLogsAsync(LogFilter condition, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    /// <summary>Null if the rule has never fired.</summary>
    Task<DateTimeOffset?> GetLastFiredAsync(Guid ruleId, CancellationToken cancellationToken);

    Task InsertEventAsync(AlertHistoryEntry entry, CancellationToken cancellationToken);

    Task<IReadOnlyList<AlertHistoryEntry>> GetHistoryAsync(Guid ruleId, int limit, CancellationToken cancellationToken);
}

/// <summary>
/// The ClickHouse seam for alert rule CRUD, evaluation queries, and fired-alert history -
/// mirrors <see cref="LogQueryService"/>'s role (the one place in this concern that holds
/// an <see cref="IClickHouseClient"/>), against <c>alert_rules</c>/<c>alert_events</c>
/// instead of <c>logs</c>.
/// </summary>
/// <remarks>
/// <c>alert_rules</c> is a <c>ReplacingMergeTree</c>, not a plain <c>MergeTree</c> like
/// <c>logs</c> - every create/update here INSERTs a brand-new row for the same
/// <see cref="AlertRule.Id"/> rather than mutating in place (<c>ALTER TABLE ...
/// UPDATE/DELETE</c> are async ClickHouse *mutations*, the wrong tool for "write, read
/// back immediately" CRUD). All reads go through <c>FROM alert_rules FINAL WHERE
/// IsDeleted = 0</c>. See db/clickhouse/0003_alert_rules.sql for the full rationale.
/// </remarks>
public sealed class AlertQueryService(IClickHouseClient client, TimeProvider timeProvider) : IAlertQueryService
{
    private const string RuleColumns =
        "Id, Name, Description, Enabled, ConditionJson, ThresholdCount, ThresholdComparator, WindowSeconds, CooldownSeconds, WebhookUrl, CreatedAt, UpdatedAt";

    public async Task<AlertRule> CreateAsync(AlertRuleRequest request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var rule = new AlertRule
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            Enabled = request.Enabled,
            Condition = request.Condition,
            Threshold = request.Threshold,
            WindowSeconds = request.WindowSeconds,
            CooldownSeconds = request.CooldownSeconds,
            WebhookUrl = request.WebhookUrl,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await InsertRuleVersionAsync(rule, isDeleted: false, cancellationToken);
        return rule;
    }

    public async Task<IReadOnlyList<AlertRule>> ListAsync(CancellationToken cancellationToken)
    {
        var sql = $"SELECT {RuleColumns} FROM alert_rules FINAL WHERE IsDeleted = 0 ORDER BY Name";
        await using var reader = await client.ExecuteReaderAsync(sql, null, SafetyOptions(), cancellationToken);
        return ReadRules(reader);
    }

    public async Task<AlertRule?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("id", id);
        var sql = $"SELECT {RuleColumns} FROM alert_rules FINAL WHERE Id = {{id:UUID}} AND IsDeleted = 0";
        await using var reader = await client.ExecuteReaderAsync(sql, parameters, SafetyOptions(), cancellationToken);
        return reader.Read() ? ReadRule(reader) : null;
    }

    public async Task<AlertRule?> UpdateAsync(Guid id, AlertRuleRequest request, CancellationToken cancellationToken)
    {
        var existing = await GetAsync(id, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        var updated = existing with
        {
            Name = request.Name,
            Description = request.Description,
            Enabled = request.Enabled,
            Condition = request.Condition,
            Threshold = request.Threshold,
            WindowSeconds = request.WindowSeconds,
            CooldownSeconds = request.CooldownSeconds,
            WebhookUrl = request.WebhookUrl,
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

    public async Task<IReadOnlyList<AlertRule>> GetEnabledRulesAsync(CancellationToken cancellationToken)
    {
        var sql = $"SELECT {RuleColumns} FROM alert_rules FINAL WHERE IsDeleted = 0 AND Enabled = 1";
        await using var reader = await client.ExecuteReaderAsync(sql, null, SafetyOptions(), cancellationToken);
        return ReadRules(reader);
    }

    public async Task<ulong> CountMatchingLogsAsync(LogFilter condition, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var windowed = condition with { From = from, To = to };
        var built = LogFilterSqlBuilder.Build(windowed, to);
        var sql = $"SELECT count() FROM logs WHERE {built.WhereSql}";
        var result = await client.ExecuteScalarAsync(sql, built.Parameters, EvaluationSafetyOptions(), cancellationToken);
        return ToUInt64(result);
    }

    public async Task<DateTimeOffset?> GetLastFiredAsync(Guid ruleId, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("ruleId", ruleId);
        // maxOrNull, not max: max() on a non-Nullable column returns the type's default
        // (the DateTime64 epoch) for zero matching rows rather than NULL, which would be
        // indistinguishable from "fired at the epoch" - the -OrNull combinator gives a
        // real NULL for "never fired" instead.
        const string sql = "SELECT maxOrNull(FiredAt) FROM alert_events WHERE RuleId = {ruleId:UUID}";
        var result = await client.ExecuteScalarAsync(sql, parameters, EvaluationSafetyOptions(), cancellationToken);
        return result is DateTime dt ? new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)) : null;
    }

    public async Task InsertEventAsync(AlertHistoryEntry entry, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("eventId", entry.EventId);
        parameters.AddParameter("ruleId", entry.RuleId);
        parameters.AddParameter("ruleName", entry.RuleName);
        parameters.AddParameter("firedAt", entry.FiredAt.UtcDateTime);
        parameters.AddParameter("observedCount", entry.ObservedCount);
        parameters.AddParameter("thresholdCount", entry.ThresholdCount);
        parameters.AddParameter("windowSeconds", (uint)entry.WindowSeconds);
        parameters.AddParameter("status", entry.NotificationStatus);
        parameters.AddParameter("statusCode", entry.NotificationStatusCode);
        parameters.AddParameter("error", entry.NotificationError);

        const string sql = """
            INSERT INTO alert_events
                (EventId, RuleId, RuleName, FiredAt, ObservedCount, ThresholdCount, WindowSeconds, NotificationStatus, NotificationStatusCode, NotificationError)
            VALUES
                ({eventId:UUID}, {ruleId:UUID}, {ruleName:String}, {firedAt:DateTime64(3)}, {observedCount:UInt64}, {thresholdCount:UInt64}, {windowSeconds:UInt32}, {status:String}, {statusCode:Int32}, {error:String})
            """;

        await client.ExecuteNonQueryAsync(sql, parameters, SafetyOptions(), cancellationToken);
    }

    public async Task<IReadOnlyList<AlertHistoryEntry>> GetHistoryAsync(Guid ruleId, int limit, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("ruleId", ruleId);
        parameters.AddParameter("limit", (uint)limit);
        const string sql = """
            SELECT EventId, RuleId, RuleName, FiredAt, ObservedCount, ThresholdCount, WindowSeconds, NotificationStatus, NotificationStatusCode, NotificationError
            FROM alert_events
            WHERE RuleId = {ruleId:UUID}
            ORDER BY FiredAt DESC
            LIMIT {limit:UInt32}
            """;

        await using var reader = await client.ExecuteReaderAsync(sql, parameters, SafetyOptions(), cancellationToken);
        var events = new List<AlertHistoryEntry>();
        while (reader.Read())
        {
            events.Add(new AlertHistoryEntry
            {
                EventId = reader.GetGuid(0),
                RuleId = reader.GetGuid(1),
                RuleName = reader.GetString(2),
                FiredAt = ReadUtc(reader, 3),
                ObservedCount = reader.GetFieldValue<ulong>(4),
                ThresholdCount = reader.GetFieldValue<ulong>(5),
                WindowSeconds = (int)reader.GetFieldValue<uint>(6),
                NotificationStatus = reader.GetString(7),
                NotificationStatusCode = reader.GetInt32(8),
                NotificationError = reader.GetString(9),
            });
        }

        return events;
    }

    private async Task InsertRuleVersionAsync(AlertRule rule, bool isDeleted, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("id", rule.Id);
        parameters.AddParameter("name", rule.Name);
        parameters.AddParameter("description", rule.Description);
        parameters.AddParameter("enabled", rule.Enabled ? (byte)1 : (byte)0);
        parameters.AddParameter("isDeleted", isDeleted ? (byte)1 : (byte)0);
        parameters.AddParameter("conditionJson", JsonSerializer.Serialize(rule.Condition, AlertsJsonContext.Default.LogFilter));
        parameters.AddParameter("thresholdCount", rule.Threshold.Count);
        parameters.AddParameter("thresholdComparator", rule.Threshold.Comparator.ToString());
        parameters.AddParameter("windowSeconds", (uint)rule.WindowSeconds);
        parameters.AddParameter("cooldownSeconds", (uint)rule.CooldownSeconds);
        parameters.AddParameter("webhookUrl", rule.WebhookUrl);
        parameters.AddParameter("createdAt", rule.CreatedAt.UtcDateTime);
        parameters.AddParameter("updatedAt", rule.UpdatedAt.UtcDateTime);

        const string sql = """
            INSERT INTO alert_rules
                (Id, Name, Description, Enabled, IsDeleted, ConditionJson, ThresholdCount, ThresholdComparator, WindowSeconds, CooldownSeconds, WebhookUrl, CreatedAt, UpdatedAt)
            VALUES
                ({id:UUID}, {name:String}, {description:String}, {enabled:UInt8}, {isDeleted:UInt8}, {conditionJson:String}, {thresholdCount:UInt64}, {thresholdComparator:String}, {windowSeconds:UInt32}, {cooldownSeconds:UInt32}, {webhookUrl:String}, {createdAt:DateTime64(3)}, {updatedAt:DateTime64(3)})
            """;

        await client.ExecuteNonQueryAsync(sql, parameters, SafetyOptions(), cancellationToken);
    }

    private static List<AlertRule> ReadRules(ClickHouseDataReader reader)
    {
        var rules = new List<AlertRule>();
        while (reader.Read())
        {
            rules.Add(ReadRule(reader));
        }

        return rules;
    }

    private static AlertRule ReadRule(ClickHouseDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        Name = reader.GetString(1),
        Description = reader.GetString(2),
        Enabled = reader.GetByte(3) != 0,
        Condition = JsonSerializer.Deserialize(reader.GetString(4), AlertsJsonContext.Default.LogFilter) ?? new LogFilter(),
        Threshold = new AlertThreshold
        {
            Count = reader.GetFieldValue<ulong>(5),
            Comparator = Enum.Parse<ThresholdComparator>(reader.GetString(6)),
        },
        WindowSeconds = (int)reader.GetFieldValue<uint>(7),
        CooldownSeconds = (int)reader.GetFieldValue<uint>(8),
        WebhookUrl = reader.GetString(9),
        CreatedAt = ReadUtc(reader, 10),
        UpdatedAt = ReadUtc(reader, 11),
    };

    /// <summary>See <see cref="LogQueryService"/>'s identical helper's remarks - same <c>DateTime64</c>/<c>Kind=Unspecified</c> driver behavior applies here.</summary>
    private static DateTimeOffset ReadUtc(ClickHouseDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    private static ulong ToUInt64(object? value) => value switch
    {
        null or DBNull => 0UL,
        ulong u => u,
        _ => Convert.ToUInt64(value),
    };

    /// <summary>Same query-safety rationale as <see cref="LogQueryService.SafetyOptions"/>, used here for rule CRUD/history.</summary>
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

    /// <summary>
    /// Tighter cap than <see cref="SafetyOptions"/> for the count/last-fired queries
    /// <c>AlertEvaluationWorker</c> runs once per rule on every poll tick - a runaway
    /// query here blocks the whole tick, not just one dashboard request.
    /// </summary>
    private static QueryOptions EvaluationSafetyOptions() => new()
    {
        CustomSettings = new Dictionary<string, object>
        {
            ["max_execution_time"] = 10,
            ["timeout_before_checking_execution_speed"] = 0,
            ["max_rows_to_read"] = 1_000_000_000,
        },
    };
}
