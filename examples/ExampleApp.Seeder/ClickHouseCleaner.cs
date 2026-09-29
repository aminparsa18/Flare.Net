using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace ExampleApp.Seeder;

/// <summary>
/// Deletes seeded telemetry straight from ClickHouse - Flare.Api has no delete endpoint for
/// telemetry, by design. Only rows whose resource carries <c>flare.seed=&lt;scenario&gt;</c> are
/// deleted, so live data (the shop demo, your own apps) is never touched.
/// </summary>
/// <remarks>
/// <para>
/// The span-derived aggregate tables (<c>service_metrics</c>, <c>service_dependency_nodes</c>,
/// <c>service_call_breakdown_*</c>, <c>outbound_calls</c>) have no resource attributes to filter
/// by - their rows are per-minute sums across every span. So after deleting seeded spans, every
/// aggregate row from the earliest deleted span's minute onward is deleted and rebuilt from the
/// spans that remain, by running each materialized view's own SELECT (read from
/// <c>system.tables.as_select</c>, so it stays in step with the migrations) over that window.
/// Live spans flushed during the rebuild itself can be counted twice or missed in those few
/// seconds' buckets - fine for a demo database, not something to run against production.
/// </para>
/// <para>
/// Single-node only: in cluster mode (db/clickhouse-cluster) the tables are Distributed over
/// <c>*_local</c> ReplicatedMergeTree tables, which need <c>ON CLUSTER</c> mutations. The
/// cleaner refuses rather than half-deleting there.
/// </para>
/// </remarks>
public sealed partial class ClickHouseCleaner(HttpClient http, Uri url, string user, string password, string database)
{
    private static readonly (string Table, string TimeColumn)[] RawTables =
    [
        ("logs", "Timestamp"), ("spans", "StartTime"), ("metrics_gauge", "Time"), ("metrics_sum", "Time"),
        ("metrics_histogram", "Time"), ("metrics_exponential_histogram", "Time"),
    ];

    public async Task ClearAsync(IReadOnlyCollection<string> scenarios, CancellationToken ct)
    {
        var tables = (await QueryAsync($"SELECT name, engine FROM system.tables WHERE database = '{database}' FORMAT TabSeparated", ct))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t'))
            .ToDictionary(cols => cols[0], cols => cols[1]);
        if (tables.GetValueOrDefault("spans") == "Distributed")
        {
            throw new InvalidOperationException(
                "This Flare runs in ClickHouse cluster mode (Distributed tables) - --clear only supports single-node. Seed with --append instead.");
        }

        var marker = string.Join(", ", scenarios.Select(s => $"'{s}'"));
        var seeded = $"ResourceAttributes['{OtlpBatch.SeedAttribute}'] IN ({marker})";

        // Where the aggregate rebuild has to start: the earliest seeded span about to go.
        var earliest = (await QueryAsync($"SELECT toUnixTimestamp(min(StartTime)) FROM {database}.spans WHERE {seeded} FORMAT TabSeparated", ct)).Trim();
        var hadSpans = long.TryParse(earliest, out var fromUnixSeconds) && fromUnixSeconds > 0;

        foreach (var (table, _) in RawTables.Where(t => tables.ContainsKey(t.Table)))
        {
            await MutateAsync($"ALTER TABLE {database}.{table} DELETE WHERE {seeded}", ct);
        }

        if (hadSpans)
        {
            await RebuildSpanAggregatesAsync(fromUnixSeconds, ct);
        }
    }

    private async Task RebuildSpanAggregatesAsync(long fromUnixSeconds, CancellationToken ct)
    {
        var from = $"toStartOfMinute(toDateTime({fromUnixSeconds}))";
        var views = (await QueryAsync(
                $"SELECT name, replaceAll(as_select, '\\n', ' '), replaceAll(create_table_query, '\\n', ' ') FROM system.tables " +
                $"WHERE database = '{database}' AND engine = 'MaterializedView' FORMAT TabSeparatedRaw", ct))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t'));

        foreach (var view in views)
        {
            var (name, select, create) = (view[0], view[1], view[2]);
            var target = TargetTable().Match(create);
            var source = SpansSource(database).Match(select);
            if (!target.Success || !source.Success)
            {
                Console.WriteLine($"  skipped rebuilding {name}: not a spans-to-table view");
                continue;
            }

            var targetTable = target.Groups[1].Value;
            var columns = (await QueryAsync(
                    $"SELECT name FROM system.columns WHERE database || '.' || table = '{targetTable}' ORDER BY position FORMAT TabSeparated", ct))
                .Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var timeColumn = columns.Contains("TimeBucket") ? "TimeBucket" : "StartTime";
            var columnList = string.Join(", ", columns);
            var windowed = select[..source.Index] + $"FROM (SELECT * FROM {database}.spans WHERE StartTime >= {from})" + select[(source.Index + source.Length)..];

            await MutateAsync($"ALTER TABLE {targetTable} DELETE WHERE {timeColumn} >= {from}", ct);
            await QueryAsync($"INSERT INTO {targetTable} ({columnList}) SELECT {columnList} FROM ({windowed})", ct);
        }
    }

    private Task MutateAsync(string sql, CancellationToken ct) => QueryAsync(sql + " SETTINGS mutations_sync = 2", ct);

    private async Task<string> QueryAsync(string sql, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(sql, Encoding.UTF8, "text/plain") };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}")));
        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return response.IsSuccessStatusCode
            ? body
            : throw new InvalidOperationException($"ClickHouse query failed ({(int)response.StatusCode}): {body.Trim()}\n  {sql}");
    }

    [GeneratedRegex(@"\bTO\s+(\S+\.\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex TargetTable();

    private static Regex SpansSource(string database) =>
        new($@"\bFROM\s+`?{Regex.Escape(database)}`?\.`?spans`?(?![\w_])", RegexOptions.IgnoreCase);
}
