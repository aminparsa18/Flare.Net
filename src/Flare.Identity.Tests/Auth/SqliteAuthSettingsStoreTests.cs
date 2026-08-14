using Flare.Identity.Auth;
using Flare.Identity.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flare.Identity.Tests.Auth;

public class SqliteAuthSettingsStoreTests : IAsyncLifetime
{
    private readonly IdentityTestDatabase _database = new();
    private SqliteAuthSettingsStore _store = null!;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        _store = new SqliteAuthSettingsStore(_database.ConnectionFactory, TimeProvider.System);
    }

    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task GetAsync_DefaultsToDisabled_OnAFreshInstallWithNoUsers()
    {
        // IdentityTestDatabase.InitializeAsync runs every migration, including
        // 0004_auth_settings.sql, against a brand-new (empty) database - this is the
        // "fresh install" case the opt-in-auth pivot is actually for.
        var settings = await _store.GetAsync();

        Assert.False(settings.Enabled);
        Assert.True(settings.LocalEnabled);
    }

    [Fact]
    public async Task Migration_SeedsEnabledTrue_WhenAUserAlreadyExisted_SimulatingAnUpgrade()
    {
        // Simulates a v11/v12 database upgrading to this migration: undo 0004's effect
        // (as if it had never run), insert a pre-existing user the way a real upgrade
        // would already have one, then re-run the migrator - exercising the *actual*
        // migration SQL's seed logic, not a reimplementation of it.
        await using (var connection = await _database.ConnectionFactory.OpenAsync())
        {
            await using (var insertUser = connection.CreateCommand())
            {
                insertUser.CommandText =
                    """
                    INSERT INTO Users (Id, Username, PasswordHash, Role, CreatedAt, UpdatedAt, IsDisabled)
                    VALUES ('11111111-1111-1111-1111-111111111111', 'preexisting-admin', 'hash', 'Admin', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z', 0)
                    """;
                await insertUser.ExecuteNonQueryAsync();
            }

            await using (var undoMigration = connection.CreateCommand())
            {
                undoMigration.CommandText =
                    """
                    DELETE FROM AuthSettings WHERE Id = 1;
                    DELETE FROM schema_migrations WHERE Name = '0004_auth_settings.sql';
                    """;
                await undoMigration.ExecuteNonQueryAsync();
            }
        }

        await IdentityMigrationRunner.ApplyAsync(_database.ConnectionFactory, NullLogger.Instance);

        var settings = await _store.GetAsync();
        Assert.True(settings.Enabled);
    }

    [Fact]
    public async Task SaveAsync_ThenGetAsync_RoundTrips()
    {
        await _store.SaveAsync(enabled: true, localEnabled: false);

        var settings = await _store.GetAsync();

        Assert.True(settings.Enabled);
        Assert.False(settings.LocalEnabled);
        Assert.NotNull(settings.UpdatedAt);
    }
}
