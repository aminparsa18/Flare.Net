using System.Data.Common;

namespace Flare.Identity.IngestKeys;

public sealed class DbIngestApiKeyStore(IdentityDbConnectionFactory connectionFactory, TimeProvider timeProvider) : IIngestApiKeyStore
{
    public async Task<(IngestApiKey Key, string RawKey)> CreateAsync(string name, Guid? projectId = null, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        var now = timeProvider.GetUtcNow();
        var rawKey = IngestApiKeyHasher.GenerateRawKey();
        var keyHash = IngestApiKeyHasher.Hash(rawKey);

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO IngestApiKeys (Id, Name, KeyHash, CreatedAt, RevokedAt, ProjectId)
            VALUES (@id, @name, @keyHash, @createdAt, NULL, @projectId)
            """;
        command.AddParameter("@id", id.ToString());
        command.AddParameter("@name", name);
        command.AddParameter("@keyHash", keyHash);
        command.AddParameter("@createdAt", now.ToString("O"));
        command.AddParameter("@projectId", (object?)projectId?.ToString() ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return (new IngestApiKey(id, name, now, RevokedAt: null) { ProjectId = projectId }, rawKey);
    }

    public async Task<IReadOnlyList<IngestApiKey>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, Name, CreatedAt, RevokedAt,
                   LimitsEnabled, MaxEventsPerMinute, MaxBytesPerMinute, MaxEventsPerDay, MaxBytesPerDay, ProjectId
            FROM IngestApiKeys ORDER BY CreatedAt
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var keys = new List<IngestApiKey>();
        while (await reader.ReadAsync(cancellationToken))
        {
            keys.Add(new IngestApiKey(
                Id: Guid.Parse(reader.GetString(0)),
                Name: reader.GetString(1),
                CreatedAt: DateTimeOffset.Parse(reader.GetString(2)),
                RevokedAt: reader.IsDBNull(3) ? null : DateTimeOffset.Parse(reader.GetString(3)))
            {
                Limits = ReadLimits(reader, firstOrdinal: 4),
                ProjectId = reader.IsDBNull(9) ? null : Guid.Parse(reader.GetString(9)),
            });
        }
        return keys;
    }

    public async Task<bool> SetProjectAsync(Guid id, Guid? projectId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE IngestApiKeys SET ProjectId = @projectId WHERE Id = @id";
        command.AddParameter("@id", id.ToString());
        command.AddParameter("@projectId", (object?)projectId?.ToString() ?? DBNull.Value);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task RevokeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE IngestApiKeys SET RevokedAt = @revokedAt WHERE Id = @id AND RevokedAt IS NULL";
        command.AddParameter("@revokedAt", timeProvider.GetUtcNow().ToString("O"));
        command.AddParameter("@id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> UpdateLimitsAsync(Guid id, IngestApiKeyLimits limits, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE IngestApiKeys
            SET LimitsEnabled = @enabled,
                MaxEventsPerMinute = @maxEventsPerMinute,
                MaxBytesPerMinute = @maxBytesPerMinute,
                MaxEventsPerDay = @maxEventsPerDay,
                MaxBytesPerDay = @maxBytesPerDay
            WHERE Id = @id
            """;
        command.AddParameter("@id", id.ToString());
        command.AddParameter("@enabled", limits.Enabled ? 1 : 0);
        command.AddParameter("@maxEventsPerMinute", (object?)limits.MaxEventsPerMinute ?? DBNull.Value);
        command.AddParameter("@maxBytesPerMinute", (object?)limits.MaxBytesPerMinute ?? DBNull.Value);
        command.AddParameter("@maxEventsPerDay", (object?)limits.MaxEventsPerDay ?? DBNull.Value);
        command.AddParameter("@maxBytesPerDay", (object?)limits.MaxBytesPerDay ?? DBNull.Value);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<IReadOnlyList<ActiveIngestApiKey>> ListActiveKeysAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, Name, KeyHash,
                   LimitsEnabled, MaxEventsPerMinute, MaxBytesPerMinute, MaxEventsPerDay, MaxBytesPerDay
            FROM IngestApiKeys WHERE RevokedAt IS NULL
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var keys = new List<ActiveIngestApiKey>();
        while (await reader.ReadAsync(cancellationToken))
        {
            keys.Add(new ActiveIngestApiKey(
                Id: Guid.Parse(reader.GetString(0)),
                Name: reader.GetString(1),
                KeyHash: reader.GetString(2),
                Limits: ReadLimits(reader, firstOrdinal: 3)));
        }
        return keys;
    }

    /// <summary>Reads the five limit columns, in migration 0016's column order, starting
    /// at <paramref name="firstOrdinal"/>.</summary>
    private static IngestApiKeyLimits ReadLimits(DbDataReader reader, int firstOrdinal)
    {
        long? Nullable(int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);

        return new IngestApiKeyLimits(
            Enabled: reader.GetInt64(firstOrdinal) != 0,
            MaxEventsPerMinute: Nullable(firstOrdinal + 1),
            MaxBytesPerMinute: Nullable(firstOrdinal + 2),
            MaxEventsPerDay: Nullable(firstOrdinal + 3),
            MaxBytesPerDay: Nullable(firstOrdinal + 4));
    }
}
