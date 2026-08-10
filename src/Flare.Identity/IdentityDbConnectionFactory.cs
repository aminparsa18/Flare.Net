using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Flare.Identity;

/// <summary>
/// Opens short-lived <see cref="SqliteConnection"/>s against the shared identity
/// database file, one per operation - matching this repo's existing ADO-style,
/// non-pooled-by-convention approach to ClickHouse (see e.g. <c>LogQueryService</c>)
/// rather than holding one long-lived connection open. <see cref="Microsoft.Data.Sqlite"/>
/// pools connections to the same connection string internally, so this is cheap.
/// </summary>
/// <remarks>
/// Every connection gets <c>PRAGMA journal_mode=WAL</c> (readers don't block on the rare
/// writer - <c>Flare.Ingest</c>'s <see cref="IngestKeys.SqliteIngestApiKeyStore"/> reads
/// coexist with <c>Flare.Api</c>'s session writes without contention) and
/// <c>PRAGMA busy_timeout=5000</c> (a writer that does collide retries for up to 5s
/// instead of throwing <c>SQLITE_BUSY</c> immediately). Write volume here is trivially
/// low - login, session create/delete, occasional user/key CRUD - so this is a cheap
/// safety margin, not a load-bearing scaling mechanism.
/// </remarks>
public sealed class IdentityDbConnectionFactory
{
    private readonly string _connectionString;

    public IdentityDbConnectionFactory(IOptions<IdentityOptions> options)
    {
        var builder = new SqliteConnectionStringBuilder { DataSource = options.Value.DbPath };
        _connectionString = builder.ToString();

        // SQLite creates the .db file itself on first connect, but not any missing
        // parent directory. docker-compose's named volume is always pre-created empty by
        // Docker, so this is a no-op there - it matters for Flare.AppHost's local-dev
        // path (a repo-relative .data/identity/ that doesn't exist until something
        // creates it) and for a bare `dotnet run` with a relative DbPath.
        var directory = Path.GetDirectoryName(options.Value.DbPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var pragmaCommand = connection.CreateCommand();
        pragmaCommand.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
        await pragmaCommand.ExecuteNonQueryAsync(cancellationToken);

        return connection;
    }
}
