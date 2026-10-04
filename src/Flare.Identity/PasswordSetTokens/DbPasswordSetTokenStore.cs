using System.Security.Cryptography;
using System.Text;

namespace Flare.Identity.PasswordSetTokens;

public sealed class DbPasswordSetTokenStore(IdentityDbConnectionFactory connectionFactory, TimeProvider timeProvider) : IPasswordSetTokenStore
{
    public async Task<IssuedPasswordSetToken> CreateAsync(Guid userId, PasswordSetPurpose purpose, TimeSpan lifetime, CancellationToken cancellationToken = default)
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var now = timeProvider.GetUtcNow();
        var expiresAt = now + lifetime;

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using (var delete = connection.CreateCommand())
        {
            delete.CommandText = "DELETE FROM PasswordSetTokens WHERE UserId = @userId";
            delete.AddParameter("@userId", userId.ToString());
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var insert = connection.CreateCommand();
        insert.CommandText =
            "INSERT INTO PasswordSetTokens (TokenHash, UserId, Purpose, CreatedAt, ExpiresAt) VALUES (@hash, @userId, @purpose, @createdAt, @expiresAt)";
        insert.AddParameter("@hash", Hash(raw));
        insert.AddParameter("@userId", userId.ToString());
        insert.AddParameter("@purpose", purpose.ToString());
        insert.AddParameter("@createdAt", now.ToString("O"));
        insert.AddParameter("@expiresAt", expiresAt.ToString("O"));
        await insert.ExecuteNonQueryAsync(cancellationToken);

        return new IssuedPasswordSetToken(raw, expiresAt);
    }

    public async Task<Guid?> ConsumeAsync(string rawToken, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // DELETE ... RETURNING is atomic on both providers, so two concurrent redemptions
        // can't both succeed.
        command.CommandText = "DELETE FROM PasswordSetTokens WHERE TokenHash = @hash RETURNING UserId, ExpiresAt";
        command.AddParameter("@hash", Hash(rawToken));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var userId = Guid.Parse(reader.GetString(0));
        var expiresAt = DateTimeOffset.Parse(reader.GetString(1));
        return expiresAt > timeProvider.GetUtcNow() ? userId : null;
    }

    private static string Hash(string raw) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}
