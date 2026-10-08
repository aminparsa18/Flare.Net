namespace Flare.AlertWorker.Archive;

/// <summary>Pure helpers for the archive export: hour windows, object keys and the INSERT INTO FUNCTION s3 statement.</summary>
public static class ArchiveSql
{
    public static IReadOnlyList<string> TablesFor(ArchiveSignal signal) => signal switch
    {
        ArchiveSignal.Logs => ["logs"],
        ArchiveSignal.Traces => ["spans"],
        _ => ["metrics_gauge", "metrics_sum", "metrics_histogram", "metrics_exponential_histogram"],
    };

    public static DateTimeOffset FloorToHour(DateTimeOffset t) =>
        new(t.Year, t.Month, t.Day, t.Hour, 0, 0, TimeSpan.Zero);

    /// <summary>Object key for one table-hour. Hive-style partitions so Athena, DuckDB and Spark prune by date.</summary>
    public static string ObjectKey(string prefix, string table, DateTimeOffset hourStart, ArchiveFormat format)
    {
        var t = hourStart.UtcDateTime;
        var ext = format == ArchiveFormat.Parquet ? "parquet" : "ndjson.gz";
        var path = $"{table}/dt={t:yyyy-MM-dd}/hh={t:HH}/{table}-{t:yyyyMMdd'T'HH}00Z.{ext}";
        var p = prefix.Trim('/');
        return p.Length == 0 ? path : $"{p}/{path}";
    }

    /// <summary>Escapes a value for a single-quoted ClickHouse string literal.</summary>
    public static string Literal(string value) => "'" + value.Replace("\\", "\\\\").Replace("'", "\\'") + "'";

    /// <summary>Rows ingested in [{from}, {to}) go to one object; rerunning overwrites it, so a retry is idempotent.</summary>
    public static string ExportSql(string endpoint, string key, string accessKey, string secretKey, string table, ArchiveFormat format)
    {
        var url = $"{endpoint.TrimEnd('/')}/{key}";
        var fmt = format == ArchiveFormat.Parquet ? "Parquet" : "JSONEachRow";
        return $$"""
            INSERT INTO FUNCTION s3({{Literal(url)}}, {{Literal(accessKey)}}, {{Literal(secretKey)}}, '{{fmt}}')
            SELECT * FROM {{table}}
            WHERE IngestedAt >= {from:DateTime64(3)} AND IngestedAt < {to:DateTime64(3)}
            SETTINGS s3_truncate_on_insert = 1, output_format_parquet_string_as_string = 1
            """;
    }

    public static string CountSql(string table) =>
        $"SELECT count() FROM {table} WHERE IngestedAt >= {{from:DateTime64(3)}} AND IngestedAt < {{to:DateTime64(3)}}";
}
