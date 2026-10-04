using Flare.Identity.Auth;
using System.Data.Common;

namespace Flare.Identity.Users;

public sealed class DbUserStore(
    IdentityDbConnectionFactory connectionFactory,
    IPasswordHasher passwordHasher,
    TimeProvider timeProvider) : IUserStore
{
    public async Task<bool> AnyAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM Users)";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result) != 0;
    }

    public async Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Id, Username, Role, CreatedAt, IsDisabled, AuthProvider, ExternalId FROM Users WHERE Id = @id";
        command.AddParameter("@id", id.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadUser(reader) : null;
    }

    public async Task<User?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // Explicit LOWER() on both sides rather than leaning on SQLite's COLLATE NOCASE
        // column collation: Postgres has no equivalent column collation, and both providers'
        // unique index on Users(Username) is case-insensitive (see the Migrations/ folders).
        command.CommandText =
            "SELECT Id, Username, Role, CreatedAt, IsDisabled, AuthProvider, ExternalId FROM Users WHERE LOWER(Username) = LOWER(@username)";
        command.AddParameter("@username", username);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadUser(reader) : null;
    }

    public async Task<User?> FindByExternalIdAsync(string authProvider, string externalId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Id, Username, Role, CreatedAt, IsDisabled, AuthProvider, ExternalId FROM Users WHERE AuthProvider = @authProvider AND ExternalId = @externalId";
        command.AddParameter("@authProvider", authProvider);
        command.AddParameter("@externalId", externalId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadUser(reader) : null;
    }

    public async Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Id, Username, Role, CreatedAt, IsDisabled, AuthProvider, ExternalId FROM Users ORDER BY LOWER(Username)";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var users = new List<User>();
        while (await reader.ReadAsync(cancellationToken))
        {
            users.Add(ReadUser(reader));
        }
        return users;
    }

    public async Task<User> CreateAsync(string username, string password, UserRole role, CancellationToken cancellationToken = default)
    {
        var passwordHash = passwordHasher.HashPassword(password);
        return await InsertAsync(username, passwordHash, role, authProvider: "Local", externalId: null, cancellationToken);
    }

    public async Task<User> CreateFromExternalAsync(string authProvider, string externalId, string username, UserRole role, CancellationToken cancellationToken = default)
    {
        // A real, well-formed hash of a random, never-revealed string - see IUserStore's
        // remarks on why this beats a hand-rolled sentinel value.
        var passwordHash = passwordHasher.HashPassword($"{Guid.NewGuid():N}{Guid.NewGuid():N}");
        return await InsertAsync(username, passwordHash, role, authProvider, externalId, cancellationToken);
    }

    public async Task<User> CreateServiceAccountAsync(string name, UserRole role, CancellationToken cancellationToken = default)
    {
        var passwordHash = passwordHasher.HashPassword($"{Guid.NewGuid():N}{Guid.NewGuid():N}");
        return await InsertAsync(name, passwordHash, role, User.ServiceAccountProvider, externalId: null, cancellationToken);
    }

    private async Task<User> InsertAsync(string username, string passwordHash, UserRole role, string authProvider, string? externalId, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var now = timeProvider.GetUtcNow();

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Users (Id, Username, PasswordHash, Role, CreatedAt, UpdatedAt, IsDisabled, AuthProvider, ExternalId)
            VALUES (@id, @username, @passwordHash, @role, @createdAt, @updatedAt, 0, @authProvider, @externalId)
            """;
        command.AddParameter("@id", id.ToString());
        command.AddParameter("@username", username);
        command.AddParameter("@passwordHash", passwordHash);
        command.AddParameter("@role", role.ToString());
        command.AddParameter("@createdAt", now.ToString("O"));
        command.AddParameter("@updatedAt", now.ToString("O"));
        command.AddParameter("@authProvider", authProvider);
        command.AddParameter("@externalId", (object?)externalId ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return new User(id, username, role, now, IsDisabled: false, AuthProvider: authProvider, ExternalId: externalId);
    }

    public async Task<User?> VerifyPasswordAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Id, Username, Role, CreatedAt, IsDisabled, AuthProvider, ExternalId, PasswordHash FROM Users WHERE LOWER(Username) = LOWER(@username)";
        command.AddParameter("@username", username);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var user = ReadUser(reader);
        var passwordHash = reader.GetString(7);

        if (user.IsDisabled || !passwordHasher.VerifyPassword(passwordHash, password))
        {
            return null;
        }

        return user;
    }

    public async Task SetDisabledAsync(Guid id, bool isDisabled, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Users SET IsDisabled = @isDisabled, UpdatedAt = @updatedAt WHERE Id = @id";
        command.AddParameter("@isDisabled", isDisabled ? 1 : 0);
        command.AddParameter("@updatedAt", timeProvider.GetUtcNow().ToString("O"));
        command.AddParameter("@id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SetPasswordAsync(Guid id, string newPassword, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Users SET PasswordHash = @hash, UpdatedAt = @updatedAt WHERE Id = @id";
        command.AddParameter("@hash", passwordHasher.HashPassword(newPassword));
        command.AddParameter("@updatedAt", timeProvider.GetUtcNow().ToString("O"));
        command.AddParameter("@id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SetRoleAsync(Guid id, UserRole role, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Users SET Role = @role, UpdatedAt = @updatedAt WHERE Id = @id";
        command.AddParameter("@role", role.ToString());
        command.AddParameter("@updatedAt", timeProvider.GetUtcNow().ToString("O"));
        command.AddParameter("@id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // Column order for every SELECT above must start with Id, Username, Role, CreatedAt,
    // IsDisabled, AuthProvider, ExternalId - VerifyPasswordAsync appends PasswordHash as
    // an 8th column it reads separately.
    private static User ReadUser(DbDataReader reader) => new(
        Id: Guid.Parse(reader.GetString(0)),
        Username: reader.GetString(1),
        Role: Enum.Parse<UserRole>(reader.GetString(2)),
        CreatedAt: DateTimeOffset.Parse(reader.GetString(3)),
        IsDisabled: reader.GetInt64(4) != 0,
        AuthProvider: reader.GetString(5),
        ExternalId: reader.IsDBNull(6) ? null : reader.GetString(6));
}
