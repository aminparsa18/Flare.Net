using Microsoft.Extensions.Logging;

namespace Flare.Identity;

/// <summary>
/// Applies every <c>Migrations/*.sql</c> file not yet recorded as applied, against the
/// shared identity SQLite database. Call <see cref="ApplyAsync"/> once at startup, from
/// both <c>Flare.Api</c> and <c>Flare.Ingest</c> (same pattern as
/// <see cref="Flare.ServiceDefaults.ClickHouseMigrations.ClickHouseMigrationRunner"/>,
/// which this mirrors) - every migration file is idempotent DDL
/// (<c>CREATE TABLE IF NOT EXISTS</c>/<c>CREATE INDEX IF NOT EXISTS</c>), so both
/// processes calling this concurrently at startup is safe without any distributed lock.
/// </summary>
/// <remarks>
/// Unlike <c>ClickHouseMigrationRunner</c>, no manual statement-splitting/comment-
/// stripping is needed: <c>Microsoft.Data.Sqlite</c>'s <c>SqliteCommand</c> natively runs
/// a full file's worth of <c>;</c>-separated statements (including <c>--</c> line
/// comments) in a single <c>ExecuteNonQueryAsync</c> call.
/// </remarks>
public static class IdentityMigrationRunner
{
    private const string ResourcePrefix = "Flare.Identity.Migrations.Sql.";

    public static async Task ApplyAsync(IdentityDbConnectionFactory connectionFactory, ILogger logger, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

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
                recordMigration.CommandText = "INSERT INTO schema_migrations (Name, AppliedAt) VALUES ($name, $appliedAt)";
                recordMigration.Parameters.AddWithValue("$name", migrationName);
                recordMigration.Parameters.AddWithValue("$appliedAt", DateTimeOffset.UtcNow.ToString("O"));
                await recordMigration.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }

    private static async Task<string> ReadEmbeddedSqlAsync(System.Reflection.Assembly assembly, string resourceName, CancellationToken cancellationToken)
    {
        using var resourceStream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded migration '{resourceName}' was listed but could not be opened.");
        using var textReader = new StreamReader(resourceStream);
        return await textReader.ReadToEndAsync(cancellationToken);
    }
}
