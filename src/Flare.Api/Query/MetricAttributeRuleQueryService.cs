using System.Text.Json;
using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.ADO.Readers;
using ClickHouse.Driver.Utility;
using Flare.Api.Json;
using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Query;

public interface IMetricAttributeRuleQueryService
{
    Task<MetricAttributeRule> CreateAsync(MetricAttributeRuleRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<MetricAttributeRule>> ListAsync(CancellationToken cancellationToken);

    Task<MetricAttributeRule?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<MetricAttributeRule?> UpdateAsync(Guid id, MetricAttributeRuleRequest request, CancellationToken cancellationToken);

    /// <summary>Soft-deletes (inserts a tombstone version) - see 0041_metric_attribute_rules.sql. Returns false if <paramref name="id"/> doesn't exist.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>ClickHouse seam for metric-attribute-rule CRUD against <c>metric_attribute_rules</c> - same tombstone/latest-version shape as <see cref="PipelineRuleQueryService"/>. <c>Flare.Ingest</c> reads the table through its own mirrored read-only store.</summary>
public sealed class MetricAttributeRuleQueryService(IClickHouseClient client, IOptions<QueryLimitsOptions> queryLimits, TimeProvider timeProvider) : IMetricAttributeRuleQueryService
{
    private const string RuleColumns = "Id, Name, Description, Enabled, MetricName, Mode, AttributesJson, CreatedAt, UpdatedAt";

    public async Task<MetricAttributeRule> CreateAsync(MetricAttributeRuleRequest request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var rule = new MetricAttributeRule
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Description = request.Description ?? "",
            Enabled = request.Enabled ?? true,
            MetricName = request.MetricName.Trim(),
            Mode = request.Mode,
            Attributes = Normalize(request.Attributes),
            CreatedAt = now,
            UpdatedAt = now,
        };

        await InsertRuleVersionAsync(rule, isDeleted: false, cancellationToken);
        return rule;
    }

    public async Task<IReadOnlyList<MetricAttributeRule>> ListAsync(CancellationToken cancellationToken)
    {
        var sql = LatestVersionSql.Select("metric_attribute_rules", RuleColumns, orderBy: "Name");
        await using var reader = await client.ExecuteReaderAsync(sql, null, SafetyOptions(), cancellationToken);
        var rules = new List<MetricAttributeRule>();
        while (reader.Read())
        {
            rules.Add(ReadRule(reader));
        }

        return rules;
    }

    public async Task<MetricAttributeRule?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("id", id);
        var sql = LatestVersionSql.Select("metric_attribute_rules", RuleColumns, idWhere: "Id = {id:UUID}");
        await using var reader = await client.ExecuteReaderAsync(sql, parameters, SafetyOptions(), cancellationToken);
        return reader.Read() ? ReadRule(reader) : null;
    }

    public async Task<MetricAttributeRule?> UpdateAsync(Guid id, MetricAttributeRuleRequest request, CancellationToken cancellationToken)
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
            Mode = request.Mode,
            Attributes = Normalize(request.Attributes),
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

        await InsertRuleVersionAsync(existing with { UpdatedAt = timeProvider.GetUtcNow() }, isDeleted: true, cancellationToken);
        return true;
    }

    private static string[] Normalize(IReadOnlyList<string>? attributes) =>
        (attributes ?? []).Select(a => a.Trim()).Distinct(StringComparer.Ordinal).ToArray();

    private async Task InsertRuleVersionAsync(MetricAttributeRule rule, bool isDeleted, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("id", rule.Id);
        parameters.AddParameter("name", rule.Name);
        parameters.AddParameter("description", rule.Description);
        parameters.AddParameter("enabled", rule.Enabled ? (byte)1 : (byte)0);
        parameters.AddParameter("isDeleted", isDeleted ? (byte)1 : (byte)0);
        parameters.AddParameter("metricName", rule.MetricName);
        parameters.AddParameter("mode", (byte)rule.Mode);
        parameters.AddParameter("attributesJson", JsonSerializer.Serialize(rule.Attributes, MetricAttributeRulesJsonContext.Default.IReadOnlyListString));
        parameters.AddParameter("createdAt", rule.CreatedAt.UtcDateTime);
        parameters.AddParameter("updatedAt", rule.UpdatedAt.UtcDateTime);

        const string sql = """
            INSERT INTO metric_attribute_rules
                (Id, Name, Description, Enabled, IsDeleted, MetricName, Mode, AttributesJson, CreatedAt, UpdatedAt)
            VALUES
                ({id:UUID}, {name:String}, {description:String}, {enabled:UInt8}, {isDeleted:UInt8}, {metricName:String}, {mode:UInt8}, {attributesJson:String}, {createdAt:DateTime64(3)}, {updatedAt:DateTime64(3)})
            """;

        await client.ExecuteNonQueryAsync(sql, parameters, SafetyOptions(), cancellationToken);
    }

    private static MetricAttributeRule ReadRule(ClickHouseDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        Name = reader.GetString(1),
        Description = reader.GetString(2),
        Enabled = reader.GetByte(3) != 0,
        MetricName = reader.GetString(4),
        Mode = (MetricAttributeRuleMode)reader.GetByte(5),
        Attributes = JsonSerializer.Deserialize(reader.GetString(6), MetricAttributeRulesJsonContext.Default.IReadOnlyListString) ?? [],
        CreatedAt = ReadUtc(reader, 7),
        UpdatedAt = ReadUtc(reader, 8),
    };

    /// <summary>See <see cref="LogQueryService"/>'s identical helper - same <c>DateTime64</c>/<c>Kind=Unspecified</c> driver behavior.</summary>
    private static DateTimeOffset ReadUtc(ClickHouseDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    private QueryOptions SafetyOptions() => QuerySafety.Full(queryLimits.Value);
}
