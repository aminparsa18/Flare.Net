using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Flare.Identity.Audit;

public sealed class SqliteAuditEventStore(IdentityDbConnectionFactory connectionFactory) : IAuditEventStore
{
    // Round-trip ("O") UTC text sorts lexicographically in time order, which is what the
    // Timestamp range filters and the prune cutoff rely on - always normalise to UTC first.
    private static string ToText(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    public async Task AppendAsync(NewAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO AuditEvents
                (Timestamp, ActorId, ActorName, ActorKind, Action, ResourceType, ResourceId, Route, StatusCode, SourceIp, Changes)
            VALUES
                ($timestamp, $actorId, $actorName, $actorKind, $action, $resourceType, $resourceId, $route, $statusCode, $sourceIp, $changes)
            """;
        command.Parameters.AddWithValue("$timestamp", ToText(auditEvent.Timestamp));
        command.Parameters.AddWithValue("$actorId", (object?)auditEvent.ActorId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$actorName", auditEvent.ActorName);
        command.Parameters.AddWithValue("$actorKind", auditEvent.ActorKind);
        command.Parameters.AddWithValue("$action", auditEvent.Action);
        command.Parameters.AddWithValue("$resourceType", auditEvent.ResourceType);
        command.Parameters.AddWithValue("$resourceId", (object?)auditEvent.ResourceId ?? DBNull.Value);
        command.Parameters.AddWithValue("$route", auditEvent.Route);
        command.Parameters.AddWithValue("$statusCode", auditEvent.StatusCode);
        command.Parameters.AddWithValue("$sourceIp", (object?)auditEvent.SourceIp ?? DBNull.Value);
        command.Parameters.AddWithValue("$changes", (object?)auditEvent.Changes ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuditEvent>> QueryAsync(
        AuditEventFilter filter,
        long? beforeId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        var where = new List<string>();
        if (filter.From is { } from)
        {
            where.Add("Timestamp >= $from");
            command.Parameters.AddWithValue("$from", ToText(from));
        }

        if (filter.To is { } to)
        {
            where.Add("Timestamp < $to");
            command.Parameters.AddWithValue("$to", ToText(to));
        }

        if (filter.ActorId is { } actorId)
        {
            where.Add("ActorId = $actorId");
            command.Parameters.AddWithValue("$actorId", actorId.ToString());
        }

        if (!string.IsNullOrEmpty(filter.ResourceType))
        {
            where.Add("ResourceType = $resourceType");
            command.Parameters.AddWithValue("$resourceType", filter.ResourceType);
        }

        if (!string.IsNullOrEmpty(filter.Action))
        {
            where.Add("Action = $action");
            command.Parameters.AddWithValue("$action", filter.Action);
        }

        if (beforeId is { } before)
        {
            where.Add("Id < $beforeId");
            command.Parameters.AddWithValue("$beforeId", before);
        }

        command.CommandText =
            "SELECT Id, Timestamp, ActorId, ActorName, ActorKind, Action, ResourceType, ResourceId, Route, StatusCode, SourceIp, Changes FROM AuditEvents"
            + (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : string.Empty)
            + " ORDER BY Id DESC LIMIT $limit";
        command.Parameters.AddWithValue("$limit", limit);

        var result = new List<AuditEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new AuditEvent(
                reader.GetInt64(0),
                DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.IsDBNull(2) ? null : Guid.Parse(reader.GetString(2)),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetString(8),
                reader.GetInt32(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11)));
        }

        return result;
    }

    public async Task<int> PruneAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM AuditEvents WHERE Timestamp < $cutoff";
        command.Parameters.AddWithValue("$cutoff", ToText(cutoff));
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
