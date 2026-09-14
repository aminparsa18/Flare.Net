using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Flare.Identity.Auth;

public sealed class SqliteLoginAttemptStore(
    IdentityDbConnectionFactory connectionFactory,
    TimeProvider timeProvider,
    IOptions<AuthOptions> authOptions) : ILoginAttemptStore
{
    public async Task<DateTimeOffset?> GetLockedUntilAsync(string username, string clientIp, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        (int FailedCount, DateTimeOffset LastFailedAt, DateTimeOffset? LockedUntil)? row;
        await using (var select = connection.CreateCommand())
        {
            select.CommandText = "SELECT FailedCount, LastFailedAt, LockedUntil FROM LoginAttempts WHERE Username = $username AND ClientIp = $clientIp";
            select.Parameters.AddWithValue("$username", username);
            select.Parameters.AddWithValue("$clientIp", clientIp);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            row = await reader.ReadAsync(cancellationToken)
                ? (reader.GetInt32(0), DateTimeOffset.Parse(reader.GetString(1)), reader.IsDBNull(2) ? null : DateTimeOffset.Parse(reader.GetString(2)))
                : null;
        }

        if (row is null || row.Value.LockedUntil is not { } lockedUntil)
        {
            // No row at all, or one still below the failure threshold (LockedUntil never
            // set) - leave it alone. Reaping it here would wipe the in-progress failure
            // count on every single check that happens *before* each attempt, which
            // would mean the count could never actually reach the threshold - found by a
            // failing lockout test, not by inspection.
            return null;
        }

        if (lockedUntil > timeProvider.GetUtcNow())
        {
            return lockedUntil;
        }

        // A lockout that's since expired - this row no longer reflects an active
        // lockout. Lazily reap it (same convention as SqliteSessionStore.FindAsync
        // reaping an expired session) so the next attempt starts a fresh streak.
        await DeleteAsync(connection, username, clientIp, cancellationToken);
        return null;
    }

    public async Task RecordFailureAsync(string username, string clientIp, CancellationToken cancellationToken = default)
    {
        var options = authOptions.Value;
        var now = timeProvider.GetUtcNow();

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        (int FailedCount, DateTimeOffset LastFailedAt)? existing;
        await using (var select = connection.CreateCommand())
        {
            select.CommandText = "SELECT FailedCount, LastFailedAt FROM LoginAttempts WHERE Username = $username AND ClientIp = $clientIp";
            select.Parameters.AddWithValue("$username", username);
            select.Parameters.AddWithValue("$clientIp", clientIp);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            existing = await reader.ReadAsync(cancellationToken) ? (reader.GetInt32(0), DateTimeOffset.Parse(reader.GetString(1))) : null;
        }

        // A prior failure older than the configured window doesn't count toward this
        // streak - see AuthOptions.LoginFailureWindow's remarks.
        var failedCount = existing is { } prior && now - prior.LastFailedAt <= options.LoginFailureWindow ? prior.FailedCount + 1 : 1;
        DateTimeOffset? lockedUntil = failedCount >= options.MaxFailedLoginAttempts ? now + options.LoginLockoutDuration : null;

        await using var upsert = connection.CreateCommand();
        upsert.CommandText =
            """
            INSERT INTO LoginAttempts (Username, ClientIp, FailedCount, LastFailedAt, LockedUntil)
            VALUES ($username, $clientIp, $failedCount, $lastFailedAt, $lockedUntil)
            ON CONFLICT (Username, ClientIp) DO UPDATE SET
                FailedCount = excluded.FailedCount,
                LastFailedAt = excluded.LastFailedAt,
                LockedUntil = excluded.LockedUntil
            """;
        upsert.Parameters.AddWithValue("$username", username);
        upsert.Parameters.AddWithValue("$clientIp", clientIp);
        upsert.Parameters.AddWithValue("$failedCount", failedCount);
        upsert.Parameters.AddWithValue("$lastFailedAt", now.ToString("O"));
        upsert.Parameters.AddWithValue("$lockedUntil", (object?)lockedUntil?.ToString("O") ?? DBNull.Value);
        await upsert.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RecordSuccessAsync(string username, string clientIp, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await DeleteAsync(connection, username, clientIp, cancellationToken);
    }

    private static async Task DeleteAsync(SqliteConnection connection, string username, string clientIp, CancellationToken cancellationToken)
    {
        await using var delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM LoginAttempts WHERE Username = $username AND ClientIp = $clientIp";
        delete.Parameters.AddWithValue("$username", username);
        delete.Parameters.AddWithValue("$clientIp", clientIp);
        await delete.ExecuteNonQueryAsync(cancellationToken);
    }
}
