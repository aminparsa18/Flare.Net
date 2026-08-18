using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Flare.Identity;

/// <summary>
/// Applies every <c>Migrations/*.sql</c> file not yet recorded as applied, against the
/// shared identity SQLite database. Call <see cref="ApplyAsync"/> once at startup, from
/// both <c>Flare.Api</c> and <c>Flare.Ingest</c>.
/// <b>Found the hard way</b> (a real `docker compose up` on a fresh volume, not a unit
/// test), twice over:
/// <list type="number">
/// <item>Two processes racing to apply the same not-yet-seen migration both used to
/// `INSERT` into <c>schema_migrations</c> with a bare `INSERT`, and the loser crashed the
/// whole process on the `Name` column's `UNIQUE` constraint. Fixed with
/// <c>INSERT OR IGNORE</c> (see <see cref="ApplyAsync"/>) - kept as cheap defense in
/// depth below even though the lock this class now takes should make it unreachable.</item>
/// <item>The bookkeeping fix above did nothing for the migration SQL itself: most
/// migration files are pure <c>CREATE TABLE IF NOT EXISTS</c>/<c>CREATE INDEX IF NOT
/// EXISTS</c> DDL and really are safe to run twice, but SQLite's `ALTER TABLE ADD COLUMN`
/// has no `IF NOT EXISTS` equivalent - two processes racing to apply
/// `0002_entra_id.sql` on a fresh volume both ran `ALTER TABLE Users ADD COLUMN
/// ExternalId ...`, and the loser crashed with `SQLite Error 1: 'duplicate column
/// name'`. Unlike
/// <see cref="Flare.ServiceDefaults.ClickHouseMigrations.ClickHouseMigrationRunner"/>
/// (whose migrations genuinely are all safe to re-run and so needs no lock), this class
/// wraps its whole body in a <c>BEGIN IMMEDIATE</c>/<c>COMMIT</c> transaction (see
/// <see cref="ApplyAsync"/>): the second process's own <c>BEGIN IMMEDIATE</c> blocks
/// until the first commits, then it re-reads <c>schema_migrations</c> inside its own
/// transaction and finds everything the winner just applied already recorded, so it
/// never re-runs the DDL at all.</item>
/// </list>
/// </summary>
/// <remarks>
/// Unlike <c>ClickHouseMigrationRunner</c>, no manual statement-splitting/comment-
/// stripping is needed: <c>Microsoft.Data.Sqlite</c>'s <c>SqliteCommand</c> natively runs
/// a full file's worth of <c>;</c>-separated statements (including <c>--</c> line
/// comments) in a single <c>ExecuteNonQueryAsync</c> call.
/// Because the whole batch now commits or rolls back together, a single broken pending
/// migration rolls back every other migration applied in the same run too (previously
/// each file auto-committed independently, with no surrounding transaction at all). This
/// is an accepted trade-off, not an oversight - every migration file here is safe to
/// retry, so a rolled-back batch just gets fully re-attempted, and re-applies cleanly,
/// on the next process restart.
/// </remarks>
public static class IdentityMigrationRunner
{
    private const string ResourcePrefix = "Flare.Identity.Migrations.Sql.";

    // Deliberately much larger than IdentityDbConnectionFactory's own 5000ms default
    // (tuned for a single hot-path app query retrying past a brief writer collision).
    // The *losing* process's BEGIN IMMEDIATE below has to wait out this whole method's
    // outer transaction - every pending migration in one batch, including a full
    // Users-table rebuild on an upgrade with real data - not just one statement. Scoped
    // to this one connection only (PRAGMA busy_timeout is per connection-handle, not
    // global) - never touches the factory's own default used by every other (hot-path)
    // connection it opens.
    private const int MigrationBusyTimeoutMilliseconds = 30_000;

    public static async Task ApplyAsync(IdentityDbConnectionFactory connectionFactory, ILogger logger, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        await ExecuteRawAsync(connection, $"PRAGMA busy_timeout={MigrationBusyTimeoutMilliseconds};", cancellationToken);

        // Governs the whole batch below now, not any single migration file - must run
        // before BEGIN (PRAGMA foreign_keys is a documented no-op once inside a
        // transaction). Previously each table-rebuild migration (0005/0008/0010) toggled
        // this itself around its own inner BEGIN TRANSACTION/COMMIT; that inner
        // transaction is gone (SQLite doesn't support nested BEGIN, and the outer
        // BEGIN IMMEDIATE below now provides the same atomicity), so this is the one
        // place foreign-key enforcement gets toggled, exactly once per call, regardless
        // of which migrations are actually pending this run.
        await ExecuteRawAsync(connection, "PRAGMA foreign_keys = OFF;", cancellationToken);

        // BEGIN IMMEDIATE (raw SQL, not SqliteConnection.BeginTransaction() - that API
        // only ever issues a plain deferred BEGIN; there is no ADO.NET-level IMMEDIATE
        // mode) acquires SQLite's single writer lock up front instead of lazily on first
        // write. A concurrent second process's own BEGIN IMMEDIATE against the same file
        // blocks (SQLITE_BUSY, retried transparently thanks to the busy_timeout above)
        // until this transaction commits or rolls back - this is the actual fix for the
        // ingest/api race documented above: the loser doesn't get a chance to start
        // applying a migration the winner already applied, it blocks until the winner
        // commits, then re-reads schema_migrations (below) inside its own transaction and
        // sees everything the winner just applied as already-done.
        await ExecuteRawAsync(connection, "BEGIN IMMEDIATE;", cancellationToken);

        try
        {
            await using (var createTrackingTable = connection.CreateCommand())
            {
                createTrackingTable.CommandText =
                    """
                    CREATE TABLE IF NOT EXISTS schema_migrations
                    (
                        Name TEXT PRIMARY KEY,
                        AppliedAt TEXT NOT NULL
                    )
                    """;
                await createTrackingTable.ExecuteNonQueryAsync(cancellationToken);
            }

            var applied = new HashSet<string>(StringComparer.Ordinal);
            await using (var selectApplied = connection.CreateCommand())
            {
                selectApplied.CommandText = "SELECT Name FROM schema_migrations";
                await using var reader = await selectApplied.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    applied.Add(reader.GetString(0));
                }
            }

            var assembly = typeof(IdentityMigrationRunner).Assembly;
            var migrationNames = assembly.GetManifestResourceNames()
                .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal))
                .Select(n => n[ResourcePrefix.Length..])
                // Numeric filename prefix (0001_, 0002_, ...) sorts correctly as plain text
                // as long as every migration keeps the same digit count - same convention/
                // caveat as ClickHouseMigrationRunner.
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            foreach (var migrationName in migrationNames)
            {
                if (applied.Contains(migrationName))
                {
                    continue;
                }

                logger.LogInformation("Applying identity migration {MigrationName}", migrationName);

                var sql = await ReadEmbeddedSqlAsync(assembly, ResourcePrefix + migrationName, cancellationToken);

                await using (var applyMigration = connection.CreateCommand())
                {
                    applyMigration.CommandText = sql;
                    await applyMigration.ExecuteNonQueryAsync(cancellationToken);
                }

                await using (var recordMigration = connection.CreateCommand())
                {
                    // OR IGNORE retained for defense-in-depth even though the outer
                    // BEGIN IMMEDIATE above should make a genuine race here impossible
                    // now - cheap, and matches this table's UNIQUE constraint on Name.
                    recordMigration.CommandText = "INSERT OR IGNORE INTO schema_migrations (Name, AppliedAt) VALUES ($name, $appliedAt)";
                    recordMigration.Parameters.AddWithValue("$name", migrationName);
                    recordMigration.Parameters.AddWithValue("$appliedAt", DateTimeOffset.UtcNow.ToString("O"));
                    await recordMigration.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            await ExecuteRawAsync(connection, "COMMIT;", cancellationToken);
        }
        catch
        {
            // CancellationToken.None: cleanup must run even if the failure was the
            // caller's own token firing - never leave this connection sitting inside an
            // open transaction going into disposal.
            await ExecuteRawAsync(connection, "ROLLBACK;", CancellationToken.None);
            throw;
        }
        finally
        {
            // Always outside the transaction by the time this runs (COMMIT above, or
            // ROLLBACK in the catch) - safe regardless of success/failure.
            await ExecuteRawAsync(connection, "PRAGMA foreign_keys = ON;", CancellationToken.None);
        }
    }

    private static async Task ExecuteRawAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<string> ReadEmbeddedSqlAsync(System.Reflection.Assembly assembly, string resourceName, CancellationToken cancellationToken)
    {
        using var resourceStream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded migration '{resourceName}' was listed but could not be opened.");
        using var textReader = new StreamReader(resourceStream);
        return await textReader.ReadToEndAsync(cancellationToken);
    }
}
