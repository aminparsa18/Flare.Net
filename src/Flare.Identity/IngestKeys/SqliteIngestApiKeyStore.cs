namespace Flare.Identity.IngestKeys;

public sealed class SqliteIngestApiKeyStore(IdentityDbConnectionFactory connectionFactory, TimeProvider timeProvider) : IIngestApiKeyStore
{
    public async Task<(IngestApiKey Key, string RawKey)> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        var now = timeProvider.GetUtcNow();
        var rawKey = IngestApiKeyHasher.GenerateRawKey();
        var keyHash = IngestApiKeyHasher.Hash(rawKey);

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO IngestApiKeys (Id, Name, KeyHash, CreatedAt, RevokedAt)
            VALUES ($id, $name, $keyHash, $createdAt, NULL)
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$keyHash", keyHash);
        command.Parameters.AddWithValue("$createdAt", now.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);

        return (new IngestApiKey(id, name, now, RevokedAt: null), rawKey);
    }

    public async Task<IReadOnlyList<IngestApiKey>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, CreatedAt, RevokedAt FROM IngestApiKeys ORDER BY CreatedAt";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var keys = new List<IngestApiKey>();
        while (await reader.ReadAsync(cancellationToken))
        {
            keys.Add(new IngestApiKey(
                Id: Guid.Parse(reader.GetString(0)),
                Name: reader.GetString(1),
                CreatedAt: DateTimeOffset.Parse(reader.GetString(2)),
                RevokedAt: reader.IsDBNull(3) ? null : DateTimeOffset.Parse(reader.GetString(3))));
        }
        return keys;
    }

    public async Task RevokeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE IngestApiKeys SET RevokedAt = $revokedAt WHERE Id = $id AND RevokedAt IS NULL";
        command.Parameters.AddWithValue("$revokedAt", timeProvider.GetUtcNow().ToString("O"));
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListActiveKeyHashesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT KeyHash FROM IngestApiKeys WHERE RevokedAt IS NULL";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var hashes = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            hashes.Add(reader.GetString(0));
        }
        return hashes;
    }
}
