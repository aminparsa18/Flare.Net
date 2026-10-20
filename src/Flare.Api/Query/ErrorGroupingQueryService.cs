using System.Text.Json;
using System.Text.RegularExpressions;
using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Errors;
using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Query;

public interface IErrorGroupingQueryService
{
    /// <summary>The stored configuration, for the editor.</summary>
    Task<ErrorGroupingDocument> GetDocumentAsync(CancellationToken cancellationToken);

    /// <summary>The configuration compiled for SQL, cached briefly: every group query asks for it.</summary>
    Task<ErrorGrouping> GetAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Asks ClickHouse whether every regular expression compiles, and rejects a replacement that would still match its own
    /// pattern or a pattern that matches an empty string (grouping has to be idempotent, see <see cref="ErrorGrouping.Sql"/>).
    /// Returns an error message, or null when the rules are usable.
    /// </summary>
    Task<string?> ValidateAsync(ErrorGroupingDocument document, CancellationToken cancellationToken);

    /// <summary>Replaces the whole configuration: the previous rules are tombstoned and the new ones inserted in order.</summary>
    Task ReplaceAsync(ErrorGroupingDocument document, string updatedBy, CancellationToken cancellationToken);
}

/// <summary>
/// The ClickHouse seam for <c>error_grouping_rules</c> (migration 0088) and the <c>errors.normalize_messages</c> row of
/// <c>runtime_settings</c> (migration 0085). Same replace-the-list shape as <see cref="ErrorOwnershipQueryService"/>.
/// </summary>
public sealed class ErrorGroupingQueryService(IClickHouseClient client, IOptions<QueryLimitsOptions> queryLimits, TimeProvider timeProvider) : IErrorGroupingQueryService
{
    private const string RuleColumns = "Id, Position, Kind, ExceptionType, Pattern, Replacement, CreatedAt";
    public const string NormalizeKey = "errors.normalize_messages";
    public static readonly Guid NormalizeId = new("e6f0c5a1-0000-4000-8000-000000000258");

    /// <summary>How long a compiled configuration is reused. Another process (the alert worker) sees a change within this long.</summary>
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(10);

    private (ErrorGrouping Grouping, DateTimeOffset ExpiresAt)? _cached;

    public async Task<ErrorGroupingDocument> GetDocumentAsync(CancellationToken cancellationToken)
    {
        var options = QuerySafety.Full(queryLimits.Value);
        var rules = new List<ErrorGroupingRule>();
        await using (var reader = await client.ExecuteReaderAsync(LatestVersionSql.Select("error_grouping_rules", RuleColumns, orderBy: "Position ASC"), null, options, cancellationToken))
        {
            while (reader.Read())
            {
                rules.Add(new ErrorGroupingRule
                {
                    Kind = Enum.TryParse<ErrorGroupingKind>(reader.GetString(2), out var kind) ? kind : ErrorGroupingKind.Replace,
                    ExceptionType = reader.GetString(3),
                    Pattern = reader.GetString(4),
                    Replacement = reader.GetString(5),
                });
            }
        }

        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("id", NormalizeId);
        var normalize = true;
        await using (var reader = await client.ExecuteReaderAsync(LatestVersionSql.Select("runtime_settings", "ValueJson", idWhere: "Id = {id:UUID}"), parameters, options, cancellationToken))
        {
            if (reader.Read() && ParseBool(reader.GetString(0)) is { } stored)
            {
                normalize = stored;
            }
        }

        return new ErrorGroupingDocument { NormalizeMessages = normalize, Rules = rules };
    }

    public async Task<ErrorGrouping> GetAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (_cached is { } cached && cached.ExpiresAt > now)
        {
            return cached.Grouping;
        }

        var grouping = ErrorGrouping.From(await GetDocumentAsync(cancellationToken));
        _cached = (grouping, now + CacheFor);
        return grouping;
    }

    public async Task<string?> ValidateAsync(ErrorGroupingDocument document, CancellationToken cancellationToken)
    {
        var options = QuerySafety.Full(queryLimits.Value);
        for (var i = 0; i < document.Rules.Count; i++)
        {
            var rule = document.Rules[i];
            if (rule.Kind != ErrorGroupingKind.Replace)
            {
                continue;
            }

            var parameters = new ClickHouseParameterCollection();
            parameters.AddParameter("pattern", rule.Pattern);
            parameters.AddParameter("replacement", rule.Replacement);
            try
            {
                await using var reader = await client.ExecuteReaderAsync(
                    "SELECT replaceRegexpAll('probe', {pattern:String}, {replacement:String}), match('', {pattern:String}), match({replacement:String}, {pattern:String})",
                    parameters, options, cancellationToken);
                reader.Read();
                if (reader.GetFieldValue<byte>(1) != 0)
                {
                    return $"rule {i + 1}: the pattern matches an empty string, which would rewrite every message.";
                }

                if (reader.GetFieldValue<byte>(2) != 0)
                {
                    return $"rule {i + 1}: the replacement matches the rule's own pattern, so the rule would keep rewriting its own output.";
                }
            }
            catch (ClickHouseServerException ex)
            {
                return $"rule {i + 1}: {FirstLine(ex.Message)}";
            }
        }

        // The whole expression, once, to catch anything the per-rule probes cannot (an unusable combination).
        try
        {
            var probe = ErrorIssueEvidenceQueryBuilder.BuildCanonicalMessages([("probe", "probe 1")], ErrorGrouping.From(document));
            await using var reader = await client.ExecuteReaderAsync(probe.Sql, probe.Parameters, options, cancellationToken);
            reader.Read();
        }
        catch (ClickHouseServerException ex)
        {
            return FirstLine(ex.Message);
        }

        return null;
    }

    public async Task ReplaceAsync(ErrorGroupingDocument document, string updatedBy, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var options = QuerySafety.Full(queryLimits.Value);

        var live = new List<(Guid Id, uint Position, string Kind, string Type, string Pattern, string Replacement, DateTime CreatedAt)>();
        await using (var reader = await client.ExecuteReaderAsync(LatestVersionSql.Select("error_grouping_rules", RuleColumns), null, options, cancellationToken))
        {
            while (reader.Read())
            {
                live.Add((reader.GetGuid(0), reader.GetFieldValue<uint>(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), DateTime.SpecifyKind(reader.GetDateTime(6), DateTimeKind.Utc)));
            }
        }

        foreach (var old in live)
        {
            await InsertRuleAsync(old.Id, old.Position, old.Kind, old.Type, old.Pattern, old.Replacement, old.CreatedAt, now, isDeleted: true, options, cancellationToken);
        }

        for (var i = 0; i < document.Rules.Count; i++)
        {
            var rule = document.Rules[i];
            await InsertRuleAsync(Guid.NewGuid(), (uint)i, rule.Kind.ToString(), rule.ExceptionType.Trim(), rule.Pattern, rule.Replacement, now, now, isDeleted: false, options, cancellationToken);
        }

        await InsertSettingAsync(document.NormalizeMessages, updatedBy, now, options, cancellationToken);
        _cached = null;
    }

    private static bool? ParseBool(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<bool?>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The server's message without its <c>Code: 36. DB::Exception: Received from host. DB::Exception:</c> wrapper or trailing query scope.</summary>
    private static string FirstLine(string message)
    {
        var line = Regex.Replace(message.Split('\n', 2)[0].Trim(), @"^(Code: \d+\. )?(DB::Exception: )?(Received from \S+\. DB::Exception: )?", "");
        var scope = line.IndexOf(": In scope", StringComparison.Ordinal);
        line = scope > 0 ? line[..scope] : line;
        return line.Length > 300 ? line[..300] : line;
    }

    private async Task InsertRuleAsync(Guid id, uint position, string kind, string exceptionType, string pattern, string replacement, DateTime createdAt, DateTime updatedAt, bool isDeleted, QueryOptions options, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("id", id);
        parameters.AddParameter("isDeleted", isDeleted ? (byte)1 : (byte)0);
        parameters.AddParameter("position", position);
        parameters.AddParameter("kind", kind);
        parameters.AddParameter("exceptionType", exceptionType);
        parameters.AddParameter("pattern", pattern);
        parameters.AddParameter("replacement", replacement);
        parameters.AddParameter("createdAt", createdAt);
        parameters.AddParameter("updatedAt", updatedAt);

        const string sql = """
            INSERT INTO error_grouping_rules (Id, IsDeleted, Position, Kind, ExceptionType, Pattern, Replacement, CreatedAt, UpdatedAt)
            VALUES ({id:UUID}, {isDeleted:UInt8}, {position:UInt32}, {kind:String}, {exceptionType:String}, {pattern:String}, {replacement:String}, {createdAt:DateTime64(3)}, {updatedAt:DateTime64(3)})
            """;

        await client.ExecuteNonQueryAsync(sql, parameters, options, cancellationToken);
    }

    private async Task InsertSettingAsync(bool normalize, string updatedBy, DateTime updatedAt, QueryOptions options, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("id", NormalizeId);
        parameters.AddParameter("key", NormalizeKey);
        parameters.AddParameter("valueJson", JsonSerializer.Serialize(normalize));
        parameters.AddParameter("updatedBy", updatedBy);
        parameters.AddParameter("updatedAt", updatedAt);

        const string sql = """
            INSERT INTO runtime_settings (Id, Key, ValueJson, IsDeleted, UpdatedBy, UpdatedAt)
            VALUES ({id:UUID}, {key:String}, {valueJson:String}, 0, {updatedBy:String}, {updatedAt:DateTime64(3)})
            """;

        await client.ExecuteNonQueryAsync(sql, parameters, options, cancellationToken);
    }
}
