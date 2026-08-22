using System.Net.Sockets;
using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Microsoft.Extensions.Logging;

namespace Flare.ServiceDefaults.ClickHouseMigrations;

/// <summary>
/// Applies every <c>db/clickhouse/*.sql</c> migration not yet recorded as applied,
/// against whatever <see cref="IClickHouseClient"/> the caller already has wired up -
/// call <see cref="ApplyAsync"/> once at startup, after <c>WebApplication.Build()</c>
/// and before the app starts accepting traffic or running hosted services that assume
/// the schema exists.
/// </summary>
/// <remarks>
/// This exists because <c>docker-entrypoint-initdb.d</c> (what <c>docker-compose.yml</c>,
/// <c>Flare.AppHost</c>, and <c>Aspire.Hosting.Flare</c>'s <c>AddFlare()</c> all mount
/// <c>db/clickhouse/</c> into) only runs once - the *first* time ClickHouse starts
/// against a completely empty data directory. That's fine for a brand-new local dev
/// volume, but confirmed live (see PR #30) that it silently does nothing for any
/// environment that already has data - which is every real deployment past its first
/// boot - no matter how many new numbered <c>.sql</c> files land in <c>db/clickhouse/</c>
/// afterward. There's no error, no warning; the new tables/columns just never appear.
/// <para>
/// The fix isn't a migration framework - every migration file already uses
/// <c>CREATE TABLE</c>/<c>ADD COLUMN IF NOT EXISTS</c> by convention (true of all 8 as
/// of this writing), so re-running the full set on every process startup is a safe,
/// fast no-op for anything already applied. This runner doesn't need a distributed
/// lock or "only the leader runs migrations" ceremony as a result - multiple replicas
/// (or both <c>Flare.Ingest</c> and <c>Flare.Api</c>) calling <see cref="ApplyAsync"/>
/// concurrently at startup is safe, because the underlying SQL is idempotent by
/// construction, not because this type does anything to serialize them. A migration
/// that stops being idempotent needs to be caught at review time - this runner doesn't
/// detect or protect against that.
/// </para>
/// <para>
/// <c>schema_migrations</c> exists for observability (a clear startup log line per
/// newly-applied file, and an auditable "what's applied and when" table) - it is not
/// the actual safety net; skip-if-already-applied is a fast path on top of SQL that
/// would already be safe to re-run unconditionally.
/// </para>
/// <para>
/// <c>docker-entrypoint-initdb.d</c> stays wired up everywhere it already was - it's a
/// harmless, now-redundant fast path for brand-new local dev volumes (skips even the
/// idempotent no-op re-execution below on first boot). This runner is what makes schema
/// currency actually reliable for every boot after that.
/// </para>
/// <para>
/// The very first statement below (the bootstrap <c>CREATE DATABASE</c>) is wrapped in a
/// connection-failure retry/backoff loop. Found 2026-08-18: even with <c>ingest</c>/
/// <c>api</c>'s <c>depends_on: clickhouse: condition: service_healthy</c> in both
/// <c>docker-compose.yml</c> and <c>docker-compose.flare.yml</c>, a fresh
/// <c>docker compose up --build</c> could still see this method throw an unhandled
/// <see cref="System.Net.Http.HttpRequestException"/> ("Connection refused") on its
/// first startup attempt - Compose's <c>service_healthy</c> gate isn't quite tight
/// enough to guarantee ClickHouse is actually accepting connections by the time the
/// dependent container's own migration code runs. Both containers also carry
/// <c>restart: unless-stopped</c>, so this was self-healing (a crash/restart cycle or
/// two) even before this retry loop existed, but retrying in-process avoids the crash
/// (and its log noise) entirely. Only the first call retries deliberately - once it
/// succeeds, ClickHouse is reachable and every later statement in this method runs
/// against an already-proven-live connection.
/// </para>
/// <para>
/// <c>clusterMode</c> (Planning.md's "Multi-node scaling" item, docs/clustering.md)
/// switches both the embedded resource set (<c>db/clickhouse-cluster/*.sql</c> instead
/// of <c>db/clickhouse/*.sql</c> - <c>ReplicatedMergeTree</c>/<c>Distributed</c> tables
/// wrapped in <c>ON CLUSTER 'flare_cluster'</c>) and the two bootstrap statements below
/// (database + <c>schema_migrations</c> table) to their cluster-aware equivalents. This
/// isn't a live migration path between the two modes - it's meant to be set once, before
/// a deployment's first boot, against an empty ClickHouse/Keeper volume.
/// </para>
/// </remarks>
public static class ClickHouseMigrationRunner
{
    private const string SingleNodeResourcePrefix = "Flare.ServiceDefaults.ClickHouseMigrations.Sql.";
    private const string ClusterResourcePrefix = "Flare.ServiceDefaults.ClickHouseMigrations.SqlCluster.";

    // Exponential backoff, capped at 8s/attempt, ~65s total across 10 attempts - well
    // past the gap observed between Compose's service_healthy and ClickHouse actually
    // accepting connections (a handful of seconds in practice), without hanging forever
    // on a genuinely dead dependency.
    private static readonly TimeSpan[] ConnectionRetryDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8),
        TimeSpan.FromSeconds(8),
        TimeSpan.FromSeconds(8),
        TimeSpan.FromSeconds(8),
        TimeSpan.FromSeconds(8),
        TimeSpan.FromSeconds(8),
        TimeSpan.FromSeconds(8),
    ];

    public static async Task ApplyAsync(IClickHouseClient client, ILogger logger, CancellationToken cancellationToken = default, bool clusterMode = false)
    {
        var resourcePrefix = clusterMode ? ClusterResourcePrefix : SingleNodeResourcePrefix;

        // Bootstrap the database + tracking table explicitly rather than assuming
        // migration 0001 (which also does "CREATE DATABASE IF NOT EXISTS clickhousedb")
        // has ever run - both statements are themselves idempotent, so this is safe to
        // run unconditionally before walking the migration file list below.
        //
        // This first call is the one wrapped in connection-failure retry (see remarks) -
        // it's the earliest point ClickHouse's actual reachability is exercised, so it's
        // where a still-starting-up ClickHouse shows up as connection-refused.
        var createDatabaseSql = clusterMode
            ? "CREATE DATABASE IF NOT EXISTS clickhousedb ON CLUSTER 'flare_cluster'"
            : "CREATE DATABASE IF NOT EXISTS clickhousedb";
        await ExecuteWithConnectionRetryAsync(
            ct => client.ExecuteNonQueryAsync(createDatabaseSql, cancellationToken: ct),
            logger,
            cancellationToken);
        await client.ExecuteNonQueryAsync(
            clusterMode
                // NOT '/clickhouse/tables/{shard}/...' like the sharded app tables below -
                // confirmed live (2026-08-22) that using the {shard} macro here means each
                // shard tracks its own independent "applied migrations" history (different
                // Keeper path per shard), so a node on shard 2 would show zero rows applied
                // even after every migration ran successfully against shard 1. This table
                // is a small, cluster-wide operational record, not sharded app data - one
                // fixed literal path (no {shard}) makes all 4 nodes replicas of the SAME
                // single table, matching what ApplyAsync's "SELECT DISTINCT Name FROM
                // schema_migrations" query actually needs regardless of which node the
                // connection string happens to point at.
                ? """
                  CREATE TABLE IF NOT EXISTS clickhousedb.schema_migrations ON CLUSTER 'flare_cluster'
                  (
                      Name String,
                      AppliedAt DateTime64(3) DEFAULT now64(3)
                  )
                  ENGINE = ReplicatedMergeTree('/clickhouse/tables/flare_cluster/clickhousedb/schema_migrations', '{replica}')
                  ORDER BY Name
                  """
                : """
                  CREATE TABLE IF NOT EXISTS clickhousedb.schema_migrations
                  (
                      Name String,
                      AppliedAt DateTime64(3) DEFAULT now64(3)
                  )
                  ENGINE = MergeTree
                  ORDER BY Name
                  """,
            cancellationToken: cancellationToken);

        var applied = new HashSet<string>(StringComparer.Ordinal);
        await using (var reader = await client.ExecuteReaderAsync(
            "SELECT DISTINCT Name FROM clickhousedb.schema_migrations",
            cancellationToken: cancellationToken))
        {
            while (reader.Read())
            {
                applied.Add(reader.GetString(0));
            }
        }

        var assembly = typeof(ClickHouseMigrationRunner).Assembly;
        var migrationNames = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(resourcePrefix, StringComparison.Ordinal))
            .Select(n => n[resourcePrefix.Length..])
            // Numeric filename prefix (0001_, 0002_, ...) sorts correctly as plain text
            // as long as every migration keeps the same digit count - true of all 10
            // today; a 10000th migration would need re-padding, not a smarter sort.
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        foreach (var migrationName in migrationNames)
        {
            if (applied.Contains(migrationName))
            {
                continue;
            }

            logger.LogInformation("Applying ClickHouse migration {MigrationName}", migrationName);

            var sql = await ReadEmbeddedSqlAsync(assembly, resourcePrefix + migrationName, cancellationToken);
            foreach (var statement in SplitStatements(sql))
            {
                await client.ExecuteNonQueryAsync(statement, cancellationToken: cancellationToken);
            }

            // migrationName comes from this assembly's own embedded resources (repo-
            // controlled filenames, never external input) - parameterized anyway to
            // match the established convention elsewhere (see
            // AlertQueryService.InsertEventAsync), not because it's actually at risk.
            var recordParameters = new ClickHouseParameterCollection();
            recordParameters.AddParameter("name", migrationName);
            await client.ExecuteNonQueryAsync(
                "INSERT INTO clickhousedb.schema_migrations (Name) VALUES ({name:String})",
                recordParameters,
                cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// Runs <paramref name="action"/>, retrying with backoff (<see cref="ConnectionRetryDelays"/>)
    /// as long as the failure looks like ClickHouse not yet accepting connections rather
    /// than a real query/schema error. See the type-level remarks for why this exists.
    /// </summary>
    private static async Task ExecuteWithConnectionRetryAsync(Func<CancellationToken, Task> action, ILogger logger, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await action(cancellationToken);
                return;
            }
            catch (Exception ex) when (attempt < ConnectionRetryDelays.Length && IsConnectionFailure(ex))
            {
                var delay = ConnectionRetryDelays[attempt];
                logger.LogWarning(
                    ex,
                    "ClickHouse not yet accepting connections (attempt {Attempt}/{MaxAttempts}); retrying in {Delay}.",
                    attempt + 1,
                    ConnectionRetryDelays.Length + 1,
                    delay);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    /// <summary>
    /// True for the transport-level failures a not-yet-ready ClickHouse produces
    /// (connection refused/reset, DNS not resolving yet) - including wrapped inside a
    /// driver-specific exception's <see cref="Exception.InnerException"/> chain. Not
    /// true for query/schema errors (bad SQL, permission denied, etc.), which should
    /// fail fast rather than retry for a minute.
    /// </summary>
    private static bool IsConnectionFailure(Exception ex) =>
        ex switch
        {
            HttpRequestException => true,
            SocketException => true,
            IOException => true,
            _ => ex.InnerException is not null && IsConnectionFailure(ex.InnerException),
        };

    private static async Task<string> ReadEmbeddedSqlAsync(System.Reflection.Assembly assembly, string resourceName, CancellationToken cancellationToken)
    {
        using var resourceStream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded migration '{resourceName}' was listed but could not be opened.");
        using var textReader = new StreamReader(resourceStream);
        return await textReader.ReadToEndAsync(cancellationToken);
    }

    /// <summary>
    /// Splits a migration file into individual statements on top-level, statement-
    /// terminating semicolons, after stripping <c>--</c> line comments.
    /// </summary>
    /// <remarks>
    /// Not a general SQL parser - safe only because every <c>db/clickhouse/*.sql</c>
    /// file is repo-authored DDL with no semicolons inside string/enum literals
    /// (confirmed by reading all 8 as of this writing, e.g. 0008_metrics.sql's
    /// <c>Enum8('...' = 0, ...)</c> definitions). A future migration that needs a
    /// literal semicolon would break this and must be caught at review time.
    /// </remarks>
    private static IEnumerable<string> SplitStatements(string sql)
    {
        var withoutComments = string.Join('\n', sql.Split('\n').Select(StripLineComment));
        foreach (var statement in withoutComments.Split(';'))
        {
            var trimmed = statement.Trim();
            if (trimmed.Length > 0)
            {
                yield return trimmed;
            }
        }
    }

    private static string StripLineComment(string line)
    {
        var index = line.IndexOf("--", StringComparison.Ordinal);
        return index < 0 ? line : line[..index];
    }
}
