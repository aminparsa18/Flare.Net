using Microsoft.Data.Sqlite;

namespace Flare.Identity.PersonalAccessTokens;

public sealed class SqlitePersonalAccessTokenStore(IdentityDbConnectionFactory connectionFactory, TimeProvider timeProvider) : IPersonalAccessTokenStore
{
    public async Task<(PersonalAccessToken Token, string RawToken)> CreateAsync(
        Guid userId, string name, DateTimeOffset? expiresAt, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        var now = timeProvider.GetUtcNow();
        var rawToken = PersonalAccessTokenHasher.GenerateRawToken();
        var tokenHash = PersonalAccessTokenHasher.Hash(rawToken);

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO PersonalAccessTokens (Id, UserId, Name, TokenHash, CreatedAt, ExpiresAt, LastUsedAt, RevokedAt)
            VALUES ($id, $userId, $name, $tokenHash, $createdAt, $expiresAt, NULL, NULL)
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$userId", userId.ToString());
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$tokenHash", tokenHash);
        command.Parameters.AddWithValue("$createdAt", now.ToString("O"));
        command.Parameters.AddWithValue("$expiresAt", (object?)expiresAt?.ToString("O") ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return (new PersonalAccessToken(id, userId, name, now, expiresAt, LastUsedAt: null, RevokedAt: null), rawToken);
    }

    public async Task<IReadOnlyList<PersonalAccessToken>> ListForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, UserId, Name, CreatedAt, ExpiresAt, LastUsedAt, RevokedAt
            FROM PersonalAccessTokens WHERE UserId = $userId ORDER BY CreatedAt DESC
            """;
        command.Parameters.AddWithValue("$userId", userId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var tokens = new List<PersonalAccessToken>();
        while (await reader.ReadAsync(cancellationToken))
        {
            tokens.Add(ReadToken(reader));
        }
        return tokens;
    }

    public async Task<PersonalAccessToken?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Id, UserId, Name, CreatedAt, ExpiresAt, LastUsedAt, RevokedAt FROM PersonalAccessTokens WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadToken(reader) : null;
    }

    public async Task RevokeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE PersonalAccessTokens SET RevokedAt = $revokedAt WHERE Id = $id AND RevokedAt IS NULL";
        command.Parameters.AddWithValue("$revokedAt", timeProvider.GetUtcNow().ToString("O"));
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<PersonalAccessToken?> ValidateAsync(string rawToken, CancellationToken cancellationToken = default)
    {
        var tokenHash = PersonalAccessTokenHasher.Hash(rawToken);

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Id, UserId, Name, CreatedAt, ExpiresAt, LastUsedAt, RevokedAt FROM PersonalAccessTokens WHERE TokenHash = $tokenHash";
        command.Parameters.AddWithValue("$tokenHash", tokenHash);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var token = ReadToken(reader);
        return token.IsActive(timeProvider.GetUtcNow()) ? token : null;
    }

    public async Task TouchLastUsedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE PersonalAccessTokens SET LastUsedAt = $lastUsedAt WHERE Id = $id";
        command.Parameters.AddWithValue("$lastUsedAt", timeProvider.GetUtcNow().ToString("O"));
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static PersonalAccessToken ReadToken(SqliteDataReader reader) => new(
        Id: Guid.Parse(reader.GetString(0)),
        UserId: Guid.Parse(reader.GetString(1)),
        Name: reader.GetString(2),
        CreatedAt: DateTimeOffset.Parse(reader.GetString(3)),
        ExpiresAt: reader.IsDBNull(4) ? null : DateTimeOffset.Parse(reader.GetString(4)),
        LastUsedAt: reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5)),
        RevokedAt: reader.IsDBNull(6) ? null : DateTimeOffset.Parse(reader.GetString(6)));
}
