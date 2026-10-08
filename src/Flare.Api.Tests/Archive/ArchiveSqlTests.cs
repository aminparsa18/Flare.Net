using Flare.AlertWorker.Archive;
using Xunit;

namespace Flare.Api.Tests.Archive;

public class ArchiveSqlTests
{
    private static readonly DateTimeOffset Hour = new(2026, 10, 8, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Floors_to_the_hour()
    {
        Assert.Equal(Hour, ArchiveSql.FloorToHour(new DateTimeOffset(2026, 10, 8, 14, 59, 31, TimeSpan.FromHours(0))));
    }

    [Theory]
    [InlineData("flare", ArchiveFormat.Parquet, "flare/logs/dt=2026-10-08/hh=14/logs-20261008T1400Z.parquet")]
    [InlineData("/a/b/", ArchiveFormat.Ndjson, "a/b/logs/dt=2026-10-08/hh=14/logs-20261008T1400Z.ndjson.gz")]
    [InlineData("", ArchiveFormat.Parquet, "logs/dt=2026-10-08/hh=14/logs-20261008T1400Z.parquet")]
    public void Builds_hive_style_keys(string prefix, ArchiveFormat format, string expected) =>
        Assert.Equal(expected, ArchiveSql.ObjectKey(prefix, "logs", Hour, format));

    [Fact]
    public void Escapes_literals()
    {
        Assert.Equal(@"'a\'b\\c'", ArchiveSql.Literal(@"a'b\c"));
    }

    [Fact]
    public void Export_sql_targets_s3_and_the_ingest_window()
    {
        var sql = ArchiveSql.ExportSql("http://rustfs:9000/b/", "k.parquet", "ak", "sk'x", "spans", ArchiveFormat.Parquet);
        Assert.Contains("s3('http://rustfs:9000/b/k.parquet', 'ak', 'sk\\'x', 'Parquet')", sql);
        Assert.Contains("FROM spans", sql);
        Assert.Contains("IngestedAt >= {from:DateTime64(3)}", sql);
    }

    [Fact]
    public void Metrics_cover_all_four_tables() => Assert.Equal(4, ArchiveSql.TablesFor(ArchiveSignal.Metrics).Count);

    [Fact]
    public void Validate_requires_endpoint_and_credentials()
    {
        Assert.NotNull(new ArchiveOptions().Validate());
        Assert.Null(new ArchiveOptions { Endpoint = "http://x/b", AccessKey = "a", SecretKey = "s" }.Validate());
    }
}
