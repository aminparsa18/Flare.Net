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
}
