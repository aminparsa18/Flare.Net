using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Flare.Identity.Tests.TestSupport;

/// <summary>A fresh, migrated identity database - construct one per test method (e.g. as a
/// field, driven by the test class itself implementing <see cref="IAsyncLifetime"/>; do
/// NOT share this via xunit's <c>IClassFixture</c>, which hands every test method in the
/// class the *same* instance and would leak state - e.g. a user created by one test
/// would be visible to another test's "no users exist yet" assertion.
/// <para>
/// Defaults to a temp-file SQLite database. Set <c>FLARE_TEST_POSTGRES</c> to an Npgsql
/// connection string for a server where the user may <c>CREATE DATABASE</c> (e.g.
/// <c>Host=localhost;Username=postgres;Password=postgres</c>) and the same store tests run
/// against Postgres instead, each test in its own throwaway database. Pass
/// <see cref="IdentityProvider.Sqlite"/> explicitly for tests that exercise SQLite-only
/// behaviour (the migration history, the Users table rebuild).</para></summary>
public sealed class IdentityTestDatabase : IAsyncLifetime
{
    private static readonly string? PostgresAdminConnectionString =
        Environment.GetEnvironmentVariable("FLARE_TEST_POSTGRES") is { Length: > 0 } value ? value : null;

    private readonly IdentityProvider _provider;
    private readonly string _name = $"flare_identity_test_{Guid.NewGuid():N}";
    private readonly string _dbPath;

    public IdentityTestDatabase(IdentityProvider? provider = null)
    {
        _provider = provider ?? (PostgresAdminConnectionString is null ? IdentityProvider.Sqlite : IdentityProvider.Postgres);
        _dbPath = Path.Combine(Path.GetTempPath(), $"{_name}.db");
    }

    public IdentityProvider Provider => _provider;

    public IdentityDbConnectionFactory ConnectionFactory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        IdentityOptions identityOptions;
        if (_provider == IdentityProvider.Postgres)
        {
            if (PostgresAdminConnectionString is null)
            {
                throw new InvalidOperationException("FLARE_TEST_POSTGRES is not set.");
            }

            await ExecuteAdminAsync($"CREATE DATABASE {_name}");
            identityOptions = new IdentityOptions
            {
                Provider = IdentityProvider.Postgres,
                ConnectionString = new NpgsqlConnectionStringBuilder(PostgresAdminConnectionString) { Database = _name }.ToString(),
            };
        }
        else
        {
            identityOptions = new IdentityOptions { DbPath = _dbPath };
        }

        ConnectionFactory = new IdentityDbConnectionFactory(Options.Create(identityOptions));
        await IdentityMigrationRunner.ApplyAsync(ConnectionFactory, NullLogger.Instance);
    }

    public async Task DisposeAsync()
    {
        if (_provider == IdentityProvider.Postgres)
        {
            // Pooled connections would keep the database "in use" - drop them first.
            NpgsqlConnection.ClearAllPools();
            await ExecuteAdminAsync($"DROP DATABASE IF EXISTS {_name} WITH (FORCE)");
            return;
        }

        // SQLite WAL mode leaves -wal/-shm sidecar files alongside the main db file -
        // best-effort cleanup, not load-bearing (TestSupport files living in the OS temp
        // dir get swept eventually regardless).
        foreach (var path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static async Task ExecuteAdminAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(PostgresAdminConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
