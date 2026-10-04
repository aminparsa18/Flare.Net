using Flare.Identity.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flare.Identity.Tests;

/// <summary>
/// Postgres-only twin of the SQLite concurrency tests in <see cref="IdentityMigrationRunnerTests"/>.
/// Runs only when <c>FLARE_TEST_POSTGRES</c> is set (see <see cref="IdentityTestDatabase"/>);
/// otherwise each test returns immediately, since xunit v2 has no runtime skip.
/// </summary>
public class PostgresMigrationRunnerTests : IAsyncLifetime
{
    private readonly bool _enabled = Environment.GetEnvironmentVariable("FLARE_TEST_POSTGRES") is { Length: > 0 };
    private readonly IdentityTestDatabase _database = new();

    public Task InitializeAsync() => _enabled ? _database.InitializeAsync() : Task.CompletedTask;

    public Task DisposeAsync() => _enabled ? _database.DisposeAsync() : Task.CompletedTask;

    [Fact]
    public async Task ApplyAsync_ConcurrentCallers_NeitherThrows_AndEachMigrationIsRecordedOnce()
    {
        if (!_enabled)
        {
            return;
        }

        // The throwaway database is already migrated once; race four more callers (Ingest +
        // several Api replicas) over it - the advisory lock serialises them.
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            IdentityMigrationRunner.ApplyAsync(_database.ConnectionFactory, NullLogger.Instance)));

        await using var connection = await _database.ConnectionFactory.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) - COUNT(DISTINCT Name) FROM schema_migrations";
        Assert.Equal(0L, Convert.ToInt64(await command.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task ApplyAsync_OnFreshDatabase_SeedsAuthSettingsDisabled()
    {
        if (!_enabled)
        {
            return;
        }

        await using var connection = await _database.ConnectionFactory.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Enabled FROM AuthSettings WHERE Id = 1";
        Assert.Equal(0L, Convert.ToInt64(await command.ExecuteScalarAsync()));
    }
}
