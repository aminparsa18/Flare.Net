using Flare.Identity.Auth;
using Microsoft.Data.Sqlite;

namespace Flare.Identity.Users;

public sealed class SqliteUserStore(
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
            "SELECT Id, Username, Role, CreatedAt, IsDisabled FROM Users WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadUser(reader) : null;
    }

    public async Task<User?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // COLLATE NOCASE on the Users.Username column (see Migrations/0001_identity.sql)
        // already makes this comparison case-insensitive - no need to lower() either side.
        command.CommandText =
            "SELECT Id, Username, Role, CreatedAt, IsDisabled FROM Users WHERE Username = $username";
        command.Parameters.AddWithValue("$username", username);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadUser(reader) : null;
    }

    public async Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Id, Username, Role, CreatedAt, IsDisabled FROM Users ORDER BY Username COLLATE NOCASE";
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
        var id = Guid.NewGuid();
        var now = timeProvider.GetUtcNow();
        var passwordHash = passwordHasher.HashPassword(password);

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Users (Id, Username, PasswordHash, Role, CreatedAt, UpdatedAt, IsDisabled)
            VALUES ($id, $username, $passwordHash, $role, $createdAt, $updatedAt, 0)
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$username", username);
        command.Parameters.AddWithValue("$passwordHash", passwordHash);
        command.Parameters.AddWithValue("$role", role.ToString());
        command.Parameters.AddWithValue("$createdAt", now.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);

        return new User(id, username, role, now, IsDisabled: false);
    }

    public async Task<User?> VerifyPasswordAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Id, Username, Role, CreatedAt, IsDisabled, PasswordHash FROM Users WHERE Username = $username";
        command.Parameters.AddWithValue("$username", username);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var user = ReadUser(reader);
        var passwordHash = reader.GetString(5);

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
        command.CommandText = "UPDATE Users SET IsDisabled = $isDisabled, UpdatedAt = $updatedAt WHERE Id = $id";
        command.Parameters.AddWithValue("$isDisabled", isDisabled ? 1 : 0);
        command.Parameters.AddWithValue("$updatedAt", timeProvider.GetUtcNow().ToString("O"));
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SetRoleAsync(Guid id, UserRole role, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Users SET Role = $role, UpdatedAt = $updatedAt WHERE Id = $id";
        command.Parameters.AddWithValue("$role", role.ToString());
        command.Parameters.AddWithValue("$updatedAt", timeProvider.GetUtcNow().ToString("O"));
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // Column order for every SELECT above must start with Id, Username, Role, CreatedAt,
    // IsDisabled - VerifyPasswordAsync appends PasswordHash as a 6th column it reads separately.
    private static User ReadUser(SqliteDataReader reader) => new(
        Id: Guid.Parse(reader.GetString(0)),
        Username: reader.GetString(1),
        Role: Enum.Parse<UserRole>(reader.GetString(2)),
        CreatedAt: DateTimeOffset.Parse(reader.GetString(3)),
        IsDisabled: reader.GetInt64(4) != 0);
}
