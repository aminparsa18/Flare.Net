using Flare.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flare.Identity.Tests.TestSupport;

/// <summary>A fresh, migrated SQLite file - construct one per test method (e.g. as a
/// field, driven by the test class itself implementing <see cref="IAsyncLifetime"/>; do
/// NOT share this via xunit's <c>IClassFixture</c>, which hands every test method in the
/// class the *same* instance and would leak state - e.g. a user created by one test
/// would be visible to another test's "no users exist yet" assertion.</summary>
public sealed class IdentityTestDatabase : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"flare-identity-test-{Guid.NewGuid():N}.db");

    public IdentityDbConnectionFactory ConnectionFactory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var options = Options.Create(new IdentityOptions { DbPath = _dbPath });
        ConnectionFactory = new IdentityDbConnectionFactory(options);
        await IdentityMigrationRunner.ApplyAsync(ConnectionFactory, NullLogger.Instance);
    }

    public Task DisposeAsync()
    {
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
        return Task.CompletedTask;
    }
}
