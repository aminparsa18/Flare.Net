using Flare.Identity.Users;

namespace Flare.Identity.Projects;

public sealed class DbProjectStore(IdentityDbConnectionFactory connectionFactory, TimeProvider timeProvider) : IProjectStore
{
    public async Task<IReadOnlyList<Project>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var patterns = await ReadPatternsAsync(connection, null, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Description, CreatedAt FROM Projects ORDER BY Name";
        var projects = new List<Project>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = Guid.Parse(reader.GetString(0));
            projects.Add(Read(reader, id, patterns.GetValueOrDefault(id) ?? []));
        }

        return projects;
    }

    public async Task<Project?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        return await GetAsync(connection, id, cancellationToken);
    }

    public async Task<Project> CreateAsync(string name, string description, IReadOnlyList<string> servicePatterns, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        if (await NameTakenAsync(connection, name, null, cancellationToken))
        {
            throw new InvalidOperationException($"A project named '{name}' already exists.");
        }

        var id = Guid.NewGuid();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO Projects (Id, Name, Description, CreatedAt) VALUES (@id, @name, @description, @createdAt)";
            command.AddParameter("@id", id.ToString());
            command.AddParameter("@name", name);
            command.AddParameter("@description", description);
            command.AddParameter("@createdAt", timeProvider.GetUtcNow().ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await ReplacePatternsAsync(connection, id, servicePatterns, cancellationToken);
        return (await GetAsync(connection, id, cancellationToken))!;
    }

    public async Task<Project?> UpdateAsync(Guid id, string name, string description, IReadOnlyList<string> servicePatterns, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        if (await GetAsync(connection, id, cancellationToken) is null)
        {
            return null;
        }

        if (await NameTakenAsync(connection, name, id, cancellationToken))
        {
            throw new InvalidOperationException($"A project named '{name}' already exists.");
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE Projects SET Name = @name, Description = @description WHERE Id = @id";
            command.AddParameter("@id", id.ToString());
            command.AddParameter("@name", name);
            command.AddParameter("@description", description);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await ReplacePatternsAsync(connection, id, servicePatterns, cancellationToken);
        return await GetAsync(connection, id, cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        // Explicit child deletes: SQLite only honours ON DELETE CASCADE with foreign_keys=ON,
        // which this connection factory doesn't promise.
        foreach (var table in new[] { "ProjectMembers", "ProjectServicePatterns" })
        {
            await using var child = connection.CreateCommand();
            child.CommandText = $"DELETE FROM {table} WHERE ProjectId = @id";
            child.AddParameter("@id", id.ToString());
            await child.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Projects WHERE Id = @id";
        command.AddParameter("@id", id.ToString());
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<IReadOnlyList<ProjectMember>> ListMembersAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT m.UserId, u.Username, m.Role
            FROM ProjectMembers m JOIN Users u ON u.Id = m.UserId
            WHERE m.ProjectId = @projectId
            ORDER BY u.Username
            """;
        command.AddParameter("@projectId", projectId.ToString());
        var members = new List<ProjectMember>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (Enum.TryParse<UserRole>(reader.GetString(2), out var role))
            {
                members.Add(new ProjectMember(Guid.Parse(reader.GetString(0)), reader.GetString(1), role));
            }
        }

        return members;
    }

    public async Task<bool> SetMemberAsync(Guid projectId, Guid userId, UserRole role, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        if (!await ExistsAsync(connection, "Projects", projectId, cancellationToken) || !await ExistsAsync(connection, "Users", userId, cancellationToken))
        {
            return false;
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO ProjectMembers (ProjectId, UserId, Role) VALUES (@projectId, @userId, @role)
            ON CONFLICT (ProjectId, UserId) DO UPDATE SET Role = excluded.Role
            """;
        command.AddParameter("@projectId", projectId.ToString());
        command.AddParameter("@userId", userId.ToString());
        command.AddParameter("@role", role.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RemoveMemberAsync(Guid projectId, Guid userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ProjectMembers WHERE ProjectId = @projectId AND UserId = @userId";
        command.AddParameter("@projectId", projectId.ToString());
        command.AddParameter("@userId", userId.ToString());
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<IReadOnlyList<ProjectMembership>> ListMembershipsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ProjectId, Role FROM ProjectMembers WHERE UserId = @userId";
        command.AddParameter("@userId", userId.ToString());
        var memberships = new List<ProjectMembership>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (Enum.TryParse<UserRole>(reader.GetString(1), out var role))
            {
                memberships.Add(new ProjectMembership(Guid.Parse(reader.GetString(0)), role));
            }
        }

        return memberships;
    }

    private static Project Read(System.Data.Common.DbDataReader reader, Guid id, IReadOnlyList<string> patterns) =>
        new(id, reader.GetString(1), reader.GetString(2), DateTimeOffset.Parse(reader.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind), patterns);

    private static async Task<Project?> GetAsync(System.Data.Common.DbConnection connection, Guid id, CancellationToken cancellationToken)
    {
        var patterns = await ReadPatternsAsync(connection, id, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Description, CreatedAt FROM Projects WHERE Id = @id";
        command.AddParameter("@id", id.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader, id, patterns.GetValueOrDefault(id) ?? []) : null;
    }

    private static async Task<Dictionary<Guid, IReadOnlyList<string>>> ReadPatternsAsync(System.Data.Common.DbConnection connection, Guid? projectId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ProjectId, Pattern FROM ProjectServicePatterns" + (projectId is null ? "" : " WHERE ProjectId = @id") + " ORDER BY Pattern";
        if (projectId is { } id)
        {
            command.AddParameter("@id", id.ToString());
        }

        var grouped = new Dictionary<Guid, List<string>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var key = Guid.Parse(reader.GetString(0));
            if (!grouped.TryGetValue(key, out var list))
            {
                grouped[key] = list = [];
            }

            list.Add(reader.GetString(1));
        }

        return grouped.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value);
    }

    private static async Task ReplacePatternsAsync(System.Data.Common.DbConnection connection, Guid projectId, IReadOnlyList<string> patterns, CancellationToken cancellationToken)
    {
        await using (var delete = connection.CreateCommand())
        {
            delete.CommandText = "DELETE FROM ProjectServicePatterns WHERE ProjectId = @id";
            delete.AddParameter("@id", projectId.ToString());
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var pattern in patterns.Distinct(StringComparer.Ordinal))
        {
            await using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO ProjectServicePatterns (ProjectId, Pattern) VALUES (@id, @pattern)";
            insert.AddParameter("@id", projectId.ToString());
            insert.AddParameter("@pattern", pattern);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<bool> NameTakenAsync(System.Data.Common.DbConnection connection, string name, Guid? exceptId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM Projects WHERE LOWER(Name) = LOWER(@name) AND Id <> @except LIMIT 1";
        command.AddParameter("@name", name);
        command.AddParameter("@except", (exceptId ?? Guid.Empty).ToString());
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task<bool> ExistsAsync(System.Data.Common.DbConnection connection, string table, Guid id, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT 1 FROM {table} WHERE Id = @id";
        command.AddParameter("@id", id.ToString());
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }
}
