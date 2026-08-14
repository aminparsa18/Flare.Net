using Flare.Identity.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flare.Identity.Tests;

public class IdentityMigrationRunnerTests : IAsyncLifetime
{
    private readonly IdentityTestDatabase _database = new();

    // IdentityTestDatabase.InitializeAsync already runs ApplyAsync once - these tests
    // exercise running it again on top of that, confirming the whole point of the
    // idempotent-migration-file convention (Migrations/0001_identity.sql uses
    // CREATE TABLE/INDEX IF NOT EXISTS throughout).
    public Task InitializeAsync() => _database.InitializeAsync();

    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task ApplyAsync_RunningTwice_DoesNotThrow_AndDoesNotDuplicateMigrationRecords()
    {
        await IdentityMigrationRunner.ApplyAsync(_database.ConnectionFactory, NullLogger.Instance);

        await using var connection = await _database.ConnectionFactory.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM schema_migrations WHERE Name = '0001_identity.sql'";
        var count = (long)(await command.ExecuteScalarAsync())!;

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task ApplyAsync_CreatesTheExpectedTables()
    {
        await using var connection = await _database.ConnectionFactory.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name";
        await using var reader = await command.ExecuteReaderAsync();

        var tables = new List<string>();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        Assert.Contains("Users", tables);
        Assert.Contains("Sessions", tables);
        Assert.Contains("IngestApiKeys", tables);
        Assert.Contains("schema_migrations", tables);
    }

    [Fact]
    public async Task ApplyAsync_UsersTableRebuild_PreservesExistingRowsAndTheirSessions()
    {
        // 0005_ldap_id.sql rebuilds the real Users table (SQLite has no ALTER TABLE ...
        // ALTER CHECK) to broaden AuthProvider's CHECK to include 'ActiveDirectory' -
        // meaningfully higher-risk than every other migration here, which are all pure
        // additive DDL. This confirms pre-existing Local/Entra rows, and a Session
        // referencing one of them, survive the rebuild byte-for-byte, and that the
        // broadened constraint is actually in effect afterward - not by asserting the
        // migration's own SQL string, but by re-running the real rebuild against real
        // pre-existing data and checking what comes out the other side.
        Guid localUserId = Guid.NewGuid(), entraUserId = Guid.NewGuid();
        const string sessionId = "session-token-for-the-local-user";

        await using (var connection = await _database.ConnectionFactory.OpenAsync())
        {
            await using (var insertUsers = connection.CreateCommand())
            {
                insertUsers.CommandText =
                    $"""
                     INSERT INTO Users (Id, Username, PasswordHash, Role, CreatedAt, UpdatedAt, IsDisabled, AuthProvider, ExternalId)
                     VALUES ('{localUserId}', 'local-admin', 'hash-1', 'Admin', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z', 0, 'Local', NULL);
                     INSERT INTO Users (Id, Username, PasswordHash, Role, CreatedAt, UpdatedAt, IsDisabled, AuthProvider, ExternalId)
                     VALUES ('{entraUserId}', 'entra-viewer', 'hash-2', 'Viewer', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z', 0, 'Entra', 'entra-oid-123');
                     """;
                await insertUsers.ExecuteNonQueryAsync();
            }

            await using (var insertSession = connection.CreateCommand())
            {
                insertSession.CommandText =
                    $"""
                     INSERT INTO Sessions (Id, UserId, CreatedAt, ExpiresAt, LastSeenAt)
                     VALUES ('{sessionId}', '{localUserId}', '2026-01-01T00:00:00Z', '2027-01-01T00:00:00Z', '2026-01-01T00:00:00Z')
                     """;
                await insertSession.ExecuteNonQueryAsync();
            }

            // Re-run the rebuild against this real pre-existing data, even though its
            // schema_migrations row already marks it applied from IdentityTestDatabase's
            // own initial ApplyAsync call.
            await using (var unmarkMigration = connection.CreateCommand())
            {
                unmarkMigration.CommandText = "DELETE FROM schema_migrations WHERE Name = '0005_ldap_id.sql'";
                await unmarkMigration.ExecuteNonQueryAsync();
            }
        }

        await IdentityMigrationRunner.ApplyAsync(_database.ConnectionFactory, NullLogger.Instance);

        await using var verifyConnection = await _database.ConnectionFactory.OpenAsync();

        await using (var selectUsers = verifyConnection.CreateCommand())
        {
            selectUsers.CommandText = "SELECT Username, AuthProvider, ExternalId FROM Users ORDER BY Username";
            await using var reader = await selectUsers.ExecuteReaderAsync();

            Assert.True(await reader.ReadAsync());
            Assert.Equal("entra-viewer", reader.GetString(0));
            Assert.Equal("Entra", reader.GetString(1));
            Assert.Equal("entra-oid-123", reader.GetString(2));

            Assert.True(await reader.ReadAsync());
            Assert.Equal("local-admin", reader.GetString(0));
            Assert.Equal("Local", reader.GetString(1));
            Assert.True(reader.IsDBNull(2));

            Assert.False(await reader.ReadAsync());
        }

        await using (var selectSession = verifyConnection.CreateCommand())
        {
            selectSession.CommandText = "SELECT UserId FROM Sessions WHERE Id = $id";
            selectSession.Parameters.AddWithValue("$id", sessionId);
            var userId = (string?)await selectSession.ExecuteScalarAsync();
            Assert.Equal(localUserId.ToString(), userId);
        }

        // The whole point of the rebuild: AuthProvider = 'ActiveDirectory' must be
        // accepted now, where the original (pre-0005) CHECK would have rejected it.
        await using (var insertAdUser = verifyConnection.CreateCommand())
        {
            insertAdUser.CommandText =
                """
                INSERT INTO Users (Id, Username, PasswordHash, Role, CreatedAt, UpdatedAt, IsDisabled, AuthProvider, ExternalId)
                VALUES ($id, 'ad-user', 'hash-3', 'Member', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z', 0, 'ActiveDirectory', 'ad-object-guid')
                """;
            insertAdUser.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            var exception = await Record.ExceptionAsync(() => insertAdUser.ExecuteNonQueryAsync());
            Assert.Null(exception);
        }
    }

    // A real bug, found live (not by a unit test): Flare.Ingest and Flare.Api both call
    // ApplyAsync at startup against the *same* SQLite file, and a fresh `docker compose
    // up` had them race to record the same not-yet-applied migration - the loser
    // crashed the whole process on schema_migrations.Name's UNIQUE constraint. Fixed
    // with INSERT OR IGNORE in ApplyAsync's own recording step. Deliberately not
    // unit-tested here: a genuine two-process race isn't reliably reproducible
    // in-process (tried a Task.WhenAll-based repro - SQLite's own file-level write
    // locking serializes two connections in the same process closely enough that the
    // exact race window this bug needs is never hit that way, unlike two independent OS
    // processes with real scheduling jitter), and a same-connection duplicate-INSERT
    // test would be circular (it would just be re-asserting what INSERT OR IGNORE
    // already guarantees by definition, never actually exercising ApplyAsync's own
    // read-then-conditionally-insert path.) Verified instead the way it broke - a real
    // `docker compose up --build` from a fresh volume - see Planning.md.
}
