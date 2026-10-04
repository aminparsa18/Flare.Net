using System.Globalization;
using System.Data.Common;

namespace Flare.Identity.Audit;

public sealed class DbAuditEventStore(IdentityDbConnectionFactory connectionFactory) : IAuditEventStore
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
                (@timestamp, @actorId, @actorName, @actorKind, @action, @resourceType, @resourceId, @route, @statusCode, @sourceIp, @changes)
            """;
        command.AddParameter("@timestamp", ToText(auditEvent.Timestamp));
        command.AddParameter("@actorId", (object?)auditEvent.ActorId?.ToString() ?? DBNull.Value);
        command.AddParameter("@actorName", auditEvent.ActorName);
        command.AddParameter("@actorKind", auditEvent.ActorKind);
        command.AddParameter("@action", auditEvent.Action);
        command.AddParameter("@resourceType", auditEvent.ResourceType);
        command.AddParameter("@resourceId", (object?)auditEvent.ResourceId ?? DBNull.Value);
        command.AddParameter("@route", auditEvent.Route);
        command.AddParameter("@statusCode", auditEvent.StatusCode);
        command.AddParameter("@sourceIp", (object?)auditEvent.SourceIp ?? DBNull.Value);
        command.AddParameter("@changes", (object?)auditEvent.Changes ?? DBNull.Value);
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
            where.Add("Timestamp >= @from");
            command.AddParameter("@from", ToText(from));
        }

        if (filter.To is { } to)
        {
            where.Add("Timestamp < @to");
            command.AddParameter("@to", ToText(to));
        }

        if (filter.ActorId is { } actorId)
        {
            where.Add("ActorId = @actorId");
            command.AddParameter("@actorId", actorId.ToString());
        }

        if (!string.IsNullOrEmpty(filter.ResourceType))
        {
            where.Add("ResourceType = @resourceType");
            command.AddParameter("@resourceType", filter.ResourceType);
        }

        if (!string.IsNullOrEmpty(filter.Action))
        {
            where.Add("Action = @action");
            command.AddParameter("@action", filter.Action);
        }

        if (beforeId is { } before)
        {
            where.Add("Id < @beforeId");
            command.AddParameter("@beforeId", before);
        }

        command.CommandText =
            "SELECT Id, Timestamp, ActorId, ActorName, ActorKind, Action, ResourceType, ResourceId, Route, StatusCode, SourceIp, Changes FROM AuditEvents"
            + (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : string.Empty)
            + " ORDER BY Id DESC LIMIT @limit";
        command.AddParameter("@limit", limit);

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
                (int)reader.GetInt64(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11)));
        }

        return result;
    }

    public async Task<int> PruneAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM AuditEvents WHERE Timestamp < @cutoff";
        command.AddParameter("@cutoff", ToText(cutoff));
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
