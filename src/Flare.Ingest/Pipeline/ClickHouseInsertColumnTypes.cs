using System.Collections.Concurrent;
using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;

namespace Flare.Ingest.Pipeline;

/// <summary>
/// Column types for <see cref="IClickHouseClient.InsertBinaryAsync(string, IEnumerable{string}, IEnumerable{object[]}, InsertOptions?, CancellationToken)"/>,
/// read from <c>system.columns</c> and passed as <see cref="InsertOptions.ColumnTypes"/>.
/// </summary>
/// <remarks>
/// Without <see cref="InsertOptions.ColumnTypes"/> the driver learns the types by running
/// <c>SELECT cols FROM table WHERE 1=0</c>, which needs SELECT on the table. Ingest connects as
/// <c>flare_ingest</c>, which has INSERT but no SELECT on the telemetry tables (ADR-0282, ADR-0283).
/// ClickHouse still lists a table's columns in <c>system.columns</c> to any user with a privilege on it,
/// so this works for <c>flare_ingest</c> and for the <c>default</c> fallback alike.
/// <para>
/// Each table is looked up once and cached. Migrations run before the flush workers start, and the
/// writers only insert columns that existed when this build shipped, so a later migration can't change
/// an entry they use. A lookup that lacks a requested column is refreshed once before failing.
/// </para>
/// </remarks>
public sealed class ClickHouseInsertColumnTypes(IClickHouseClient client)
{
    private const string ColumnsSql =
        "SELECT name, type FROM system.columns WHERE database = currentDatabase() AND table = {table:String}";

    private readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyDictionary<string, string>>>> cache = new(StringComparer.Ordinal);

    /// <summary>Insert options carrying the types of <paramref name="columns"/> in <paramref name="table"/>.</summary>
    public async Task<InsertOptions> OptionsAsync(
        string table,
        IReadOnlyList<string> columns,
        IDictionary<string, object>? customSettings = null,
        CancellationToken cancellationToken = default)
    {
        var types = await GetAsync(table, columns, cancellationToken);
        return customSettings is null
            ? new InsertOptions { ColumnTypes = types }
            : new InsertOptions { ColumnTypes = types, CustomSettings = customSettings };
    }

    internal async Task<IReadOnlyDictionary<string, string>> GetAsync(string table, IReadOnlyList<string> columns, CancellationToken cancellationToken)
    {
        var types = await LoadCachedAsync(table);
        if (MissingColumn(types, columns) is null)
        {
            return types;
        }

        cache.TryRemove(table, out _);
        types = await LoadCachedAsync(table);
        var missing = MissingColumn(types, columns);
        if (missing is not null)
        {
            throw new InvalidOperationException(
                $"Column '{missing}' of table '{table}' isn't in system.columns. Either the schema is behind this build, " +
                "or the ClickHouse user has no privilege on the table (check flare-users.xml).");
        }

        return types;

        Task<IReadOnlyDictionary<string, string>> LoadCachedAsync(string name) =>
            cache.GetOrAdd(name, n => new Lazy<Task<IReadOnlyDictionary<string, string>>>(() => LoadAsync(n, cancellationToken))).Value;
    }

    public static string? MissingColumn(IReadOnlyDictionary<string, string> types, IReadOnlyList<string> columns)
    {
        foreach (var column in columns)
        {
            if (!types.ContainsKey(column))
            {
                return column;
            }
        }

        return null;
    }

    private async Task<IReadOnlyDictionary<string, string>> LoadAsync(string table, CancellationToken cancellationToken)
    {
        try
        {
            var parameters = new ClickHouseParameterCollection();
            parameters.AddParameter("table", table);
            var types = new Dictionary<string, string>(StringComparer.Ordinal);
            await using var reader = await client.ExecuteReaderAsync(ColumnsSql, parameters, cancellationToken: cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                types[reader.GetString(0)] = reader.GetString(1);
            }

            return types;
        }
        catch
        {
            // Don't cache a failure (ClickHouse briefly unreachable): the next batch retries the lookup.
            cache.TryRemove(table, out _);
            throw;
        }
    }
}
