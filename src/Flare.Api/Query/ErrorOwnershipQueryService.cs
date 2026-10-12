using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Query;

public interface IErrorOwnershipQueryService
{
    /// <summary>The rules in match order.</summary>
    Task<IReadOnlyList<ErrorOwnershipRule>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Replaces the whole rule list: the previous rules are tombstoned and the new ones inserted in order.</summary>
    Task ReplaceAsync(IReadOnlyList<ErrorOwnershipRule> rules, CancellationToken cancellationToken);
}

/// <summary>
/// The ClickHouse seam for <c>error_ownership_rules</c> - the CRUD-via-tombstone /
/// <see cref="LatestVersionSql"/> pattern of <see cref="ErrorIssueQueryService"/>. A save replaces
/// the list as a unit, so a rule's <c>Id</c> is internal and the order is the <c>Position</c> column.
/// </summary>
public sealed class ErrorOwnershipQueryService(IClickHouseClient client, IOptions<QueryLimitsOptions> queryLimits, TimeProvider timeProvider) : IErrorOwnershipQueryService
{
    private const string RuleColumns = "Id, Position, Kind, Pattern, Owner, CreatedAt";

    public async Task<IReadOnlyList<ErrorOwnershipRule>> ListAsync(CancellationToken cancellationToken)
    {
        var sql = LatestVersionSql.Select("error_ownership_rules", RuleColumns, orderBy: "Position ASC");
        await using var reader = await client.ExecuteReaderAsync(sql, null, QuerySafety.Full(queryLimits.Value), cancellationToken);
        var rules = new List<ErrorOwnershipRule>();
        while (reader.Read())
        {
            rules.Add(new ErrorOwnershipRule
            {
                Kind = Enum.TryParse<ErrorOwnershipKind>(reader.GetString(2), out var kind) ? kind : ErrorOwnershipKind.Service,
                Pattern = reader.GetString(3),
                Owner = reader.GetString(4),
            });
        }

        return rules;
    }

    public async Task ReplaceAsync(IReadOnlyList<ErrorOwnershipRule> rules, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var options = QuerySafety.Full(queryLimits.Value);

        var live = new List<(Guid Id, uint Position, string Kind, string Pattern, string Owner, DateTime CreatedAt)>();
        var sql = LatestVersionSql.Select("error_ownership_rules", RuleColumns);
        await using (var reader = await client.ExecuteReaderAsync(sql, null, options, cancellationToken))
        {
            while (reader.Read())
            {
                live.Add((reader.GetGuid(0), reader.GetFieldValue<uint>(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc)));
            }
        }

        foreach (var old in live)
        {
            await InsertAsync(old.Id, old.Position, old.Kind, old.Pattern, old.Owner, old.CreatedAt, now, isDeleted: true, options, cancellationToken);
        }

        for (var i = 0; i < rules.Count; i++)
        {
            await InsertAsync(Guid.NewGuid(), (uint)i, rules[i].Kind.ToString(), rules[i].Pattern.Trim(), rules[i].Owner.Trim(), now, now, isDeleted: false, options, cancellationToken);
        }
    }

    private async Task InsertAsync(Guid id, uint position, string kind, string pattern, string owner, DateTime createdAt, DateTime updatedAt, bool isDeleted, QueryOptions options, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("id", id);
        parameters.AddParameter("isDeleted", isDeleted ? (byte)1 : (byte)0);
        parameters.AddParameter("position", position);
        parameters.AddParameter("kind", kind);
        parameters.AddParameter("pattern", pattern);
        parameters.AddParameter("owner", owner);
        parameters.AddParameter("createdAt", createdAt);
        parameters.AddParameter("updatedAt", updatedAt);

        const string sql = """
            INSERT INTO error_ownership_rules (Id, IsDeleted, Position, Kind, Pattern, Owner, CreatedAt, UpdatedAt)
            VALUES ({id:UUID}, {isDeleted:UInt8}, {position:UInt32}, {kind:String}, {pattern:String}, {owner:String}, {createdAt:DateTime64(3)}, {updatedAt:DateTime64(3)})
            """;

        await client.ExecuteNonQueryAsync(sql, parameters, options, cancellationToken);
    }
}
