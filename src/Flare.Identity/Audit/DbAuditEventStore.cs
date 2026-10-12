using System.Globalization;
using System.Data.Common;

namespace Flare.Identity.Audit;

public sealed class DbAuditEventStore(IdentityDbConnectionFactory connectionFactory) : IAuditEventStore
{
    // Round-trip ("O") UTC text sorts lexicographically in time order, which is what the
    // Timestamp range filters and the prune cutoff rely on - always normalise to UTC first.
    private static string ToText(DateTimeOffset value) => AuditHash.TimestampText(value);

    // Appends read the chain head and insert the next link, so they must not interleave.
    // The semaphore covers this process; Postgres (several Api replicas can share it) also
    // takes a transaction-scoped advisory lock. SQLite is single-writer by design (ADR-0004).
    private static readonly SemaphoreSlim AppendLock = new(1, 1);
    private const long PostgresAdvisoryLockKey = 0x41554449; // "AUDI"

    public async Task AppendAsync(NewAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        await AppendLock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await connectionFactory.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            if (connectionFactory.Provider == IdentityProvider.Postgres)
            {
                await using var lockCommand = connection.CreateCommand();
                lockCommand.Transaction = transaction;
                lockCommand.CommandText = "SELECT pg_advisory_xact_lock(@key)";
                lockCommand.AddParameter("@key", PostgresAdvisoryLockKey);
                await lockCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            string prevHash;
            await using (var headCommand = connection.CreateCommand())
            {
                headCommand.Transaction = transaction;
                headCommand.CommandText = "SELECT Hash FROM AuditEvents WHERE Hash IS NOT NULL ORDER BY Id DESC LIMIT 1";
                prevHash = (await headCommand.ExecuteScalarAsync(cancellationToken)) as string ?? string.Empty;
            }

            var timestamp = ToText(auditEvent.Timestamp);
            var actorId = auditEvent.ActorId?.ToString();
            var hash = AuditHash.Compute(
                prevHash, timestamp, actorId, auditEvent.ActorName, auditEvent.ActorKind, auditEvent.Action,
                auditEvent.ResourceType, auditEvent.ResourceId, auditEvent.Route, auditEvent.StatusCode,
                auditEvent.SourceIp, auditEvent.Changes);

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO AuditEvents
                    (Timestamp, ActorId, ActorName, ActorKind, Action, ResourceType, ResourceId, Route, StatusCode, SourceIp, Changes, PrevHash, Hash)
                VALUES
                    (@timestamp, @actorId, @actorName, @actorKind, @action, @resourceType, @resourceId, @route, @statusCode, @sourceIp, @changes, @prevHash, @hash)
                """;
            command.AddParameter("@timestamp", timestamp);
            command.AddParameter("@actorId", (object?)actorId ?? DBNull.Value);
            command.AddParameter("@actorName", auditEvent.ActorName);
            command.AddParameter("@actorKind", auditEvent.ActorKind);
            command.AddParameter("@action", auditEvent.Action);
            command.AddParameter("@resourceType", auditEvent.ResourceType);
            command.AddParameter("@resourceId", (object?)auditEvent.ResourceId ?? DBNull.Value);
            command.AddParameter("@route", auditEvent.Route);
            command.AddParameter("@statusCode", auditEvent.StatusCode);
            command.AddParameter("@sourceIp", (object?)auditEvent.SourceIp ?? DBNull.Value);
            command.AddParameter("@changes", (object?)auditEvent.Changes ?? DBNull.Value);
            command.AddParameter("@prevHash", prevHash);
            command.AddParameter("@hash", hash);
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            AppendLock.Release();
        }
    }

    public async Task<AuditChainVerification> VerifyChainAsync(CancellationToken cancellationToken = default)
    {
        const int batchSize = 1000;
        long afterId = 0;
        long chained = 0;
        long legacy = 0;
        string? previousHash = null;

        while (true)
        {
            await using var connection = await connectionFactory.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT Id, Timestamp, ActorId, ActorName, ActorKind, Action, ResourceType, ResourceId, Route, StatusCode, SourceIp, Changes, PrevHash, Hash"
                + " FROM AuditEvents WHERE Id > @afterId ORDER BY Id ASC LIMIT @limit";
            command.AddParameter("@afterId", afterId);
            command.AddParameter("@limit", batchSize);

            var read = 0;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                read++;
                var id = reader.GetInt64(0);
                afterId = id;
                var storedPrev = reader.IsDBNull(12) ? null : reader.GetString(12);
                var storedHash = reader.IsDBNull(13) ? null : reader.GetString(13);

                if (storedHash is null)
                {
                    if (previousHash is not null)
                    {
                        return new AuditChainVerification(false, chained, legacy, id, "A row inside the chain has no hash.", previousHash);
                    }

                    legacy++;
                    continue;
                }

                // The first chained row anchors the walk: its PrevHash points at rows retention
                // has already deleted (or is empty), so only later links can be checked.
                if (previousHash is not null && storedPrev != previousHash)
                {
                    return new AuditChainVerification(false, chained, legacy, id, "The row does not link to the previous row (a row was removed or reordered).", previousHash);
                }

                var expected = AuditHash.Compute(
                    storedPrev ?? string.Empty,
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.GetString(8),
                    reader.GetInt64(9),
                    reader.IsDBNull(10) ? null : reader.GetString(10),
                    reader.IsDBNull(11) ? null : reader.GetString(11));

                if (!string.Equals(expected, storedHash, StringComparison.Ordinal))
                {
                    return new AuditChainVerification(false, chained, legacy, id, "The row's content does not match its hash.", previousHash);
                }

                previousHash = storedHash;
                chained++;
            }

            if (read < batchSize)
            {
                break;
            }
        }

        return new AuditChainVerification(true, chained, legacy, null, null, previousHash);
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
            "SELECT Id, Timestamp, ActorId, ActorName, ActorKind, Action, ResourceType, ResourceId, Route, StatusCode, SourceIp, Changes, PrevHash, Hash FROM AuditEvents"
            + (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : string.Empty)
            + " ORDER BY Id DESC LIMIT @limit";
        command.AddParameter("@limit", limit);

        return await ReadEventsAsync(command, cancellationToken);
    }

    private const string EventColumns =
        "Id, Timestamp, ActorId, ActorName, ActorKind, Action, ResourceType, ResourceId, Route, StatusCode, SourceIp, Changes, PrevHash, Hash";

    public async Task<IReadOnlyList<AuditEvent>> QueryAfterAsync(long afterId, int limit, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {EventColumns} FROM AuditEvents WHERE Id > @afterId ORDER BY Id ASC LIMIT @limit";
        command.AddParameter("@afterId", afterId);
        command.AddParameter("@limit", limit);
        return await ReadEventsAsync(command, cancellationToken);
    }

    public async Task<long> GetForwardingCursorAsync(string sink, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT LastId FROM AuditForwardingCursor WHERE Sink = @sink";
        command.AddParameter("@sink", sink);
        return await command.ExecuteScalarAsync(cancellationToken) is { } value and not DBNull ? Convert.ToInt64(value, CultureInfo.InvariantCulture) : 0;
    }

    public async Task SetForwardingCursorAsync(string sink, long lastId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO AuditForwardingCursor (Sink, LastId) VALUES (@sink, @lastId) "
            + "ON CONFLICT (Sink) DO UPDATE SET LastId = excluded.LastId";
        command.AddParameter("@sink", sink);
        command.AddParameter("@lastId", lastId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<AuditEvent>> ReadEventsAsync(DbCommand command, CancellationToken cancellationToken)
    {
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
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetString(13)));
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
