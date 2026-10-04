using System.Security.Cryptography;
using System.Data.Common;

namespace Flare.Identity.Auth;

public sealed class DbSessionStore(IdentityDbConnectionFactory connectionFactory, TimeProvider timeProvider) : ISessionStore
{
    public async Task<Session> CreateAsync(Guid userId, TimeSpan lifetime, CancellationToken cancellationToken = default)
    {
        var token = GenerateToken();
        var now = timeProvider.GetUtcNow();
        var expiresAt = now + lifetime;

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Sessions (Id, UserId, CreatedAt, ExpiresAt, LastSeenAt)
            VALUES (@id, @userId, @createdAt, @expiresAt, @lastSeenAt)
            """;
        command.AddParameter("@id", token);
        command.AddParameter("@userId", userId.ToString());
        command.AddParameter("@createdAt", now.ToString("O"));
        command.AddParameter("@expiresAt", expiresAt.ToString("O"));
        command.AddParameter("@lastSeenAt", now.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);

        return new Session(token, userId, now, expiresAt, now);
    }

    public async Task<Session?> FindAsync(string token, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        Session? session;
        await using (var select = connection.CreateCommand())
        {
            select.CommandText = "SELECT Id, UserId, CreatedAt, ExpiresAt, LastSeenAt FROM Sessions WHERE Id = @id";
            select.AddParameter("@id", token);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            session = await reader.ReadAsync(cancellationToken) ? ReadSession(reader) : null;
        }

        if (session is null)
        {
            return null;
        }

        if (session.ExpiresAt <= timeProvider.GetUtcNow())
        {
            // Lazily reap the expired row on lookup - a background sweep isn't required
            // for correctness, only to keep the table from accumulating stale rows.
            await DeleteAsync(token, cancellationToken);
            return null;
        }

        return session;
    }

    public async Task DeleteAsync(string token, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Sessions WHERE Id = @id";
        command.AddParameter("@id", token);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAllForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Sessions WHERE UserId = @userId";
        command.AddParameter("@userId", userId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task TouchLastSeenAsync(string token, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Sessions SET LastSeenAt = @lastSeenAt WHERE Id = @id";
        command.AddParameter("@lastSeenAt", timeProvider.GetUtcNow().ToString("O"));
        command.AddParameter("@id", token);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>256-bit CSPRNG token, base64url-encoded (URL/cookie-safe, no padding).</summary>
    private static string GenerateToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static Session ReadSession(DbDataReader reader) => new(
        Id: reader.GetString(0),
        UserId: Guid.Parse(reader.GetString(1)),
        CreatedAt: DateTimeOffset.Parse(reader.GetString(2)),
        ExpiresAt: DateTimeOffset.Parse(reader.GetString(3)),
        LastSeenAt: DateTimeOffset.Parse(reader.GetString(4)));
}
