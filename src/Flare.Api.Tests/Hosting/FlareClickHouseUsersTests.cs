using System.Xml.Linq;
using Flare.ServiceDefaults.ClickHouseClients;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Flare.Api.Tests.Hosting;

/// <summary>
/// The per-service ClickHouse connection strings and the users files behind them (ADR-0282, ADR-0283).
/// Whether each grant is enough is only provable against a real server; these pin the fallback rule and
/// keep the single-node and cluster files from drifting apart.
/// </summary>
public class FlareClickHouseUsersTests
{
    private const string Ddl = "Host=clickhouse;Username=default;Password=d";
    private const string Query = "Host=clickhouse;Username=flare_query;Password=q";

    [Fact]
    public void Resolve_PrefersTheServiceConnectionString()
    {
        var config = Config(("ConnectionStrings:clickhousedb", Ddl), ("ConnectionStrings:clickhousedb-query", Query));

        Assert.Equal(Query, FlareClickHouse.ResolveConnectionString(config, FlareClickHouse.QueryConnectionName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_FallsBackToClickhousedb_WhenUnsetOrEmpty(string? own)
    {
        var config = Config(("ConnectionStrings:clickhousedb", Ddl), ("ConnectionStrings:clickhousedb-app", own));

        Assert.Equal(Ddl, FlareClickHouse.ResolveConnectionString(config, FlareClickHouse.AppConnectionName));
    }

    [Fact]
    public void Resolve_IsNull_WhenNeitherIsSet()
    {
        Assert.Null(FlareClickHouse.ResolveConnectionString(Config(), FlareClickHouse.IngestConnectionName));
    }

    [Fact]
    public void ClusterUsersFile_DiffersOnlyInTheClusterGrants()
    {
        var single = Grants(Load("db/clickhouse/config/flare-users.xml"));
        var cluster = Grants(Load("db/clickhouse-cluster/config/flare-users.xml"));

        Assert.Equal(["flare_app", "flare_ingest", "flare_query"], single.Keys.Order());
        Assert.Equal(single.Keys.Order(), cluster.Keys.Order());
        // Ingest's config reads go through Distributed tables, which read the _local tables as the same user.
        Assert.Equal(
            single["flare_ingest"].Where(g => g.StartsWith("GRANT SELECT ON clickhousedb.", StringComparison.Ordinal)).Select(g => g + "_local").Order(),
            cluster["flare_ingest"].Except(single["flare_ingest"]).Order());
        Assert.Empty(single["flare_ingest"].Except(cluster["flare_ingest"]));
        Assert.Equal(single["flare_query"], cluster["flare_query"]);
        Assert.Equal(
            ["GRANT CLUSTER ON *.*", "GRANT READ ON REMOTE"],
            cluster["flare_app"].Except(single["flare_app"]).Order());
        Assert.Empty(single["flare_app"].Except(cluster["flare_app"]));
    }

    [Fact]
    public void HelmChartCopy_MatchesTheSingleNodeUsersFile()
    {
        // Helm can't read files outside the chart, so the chart carries its own copy.
        Assert.Equal(
            File.ReadAllText(Path.Combine(RepoRoot(), "db/clickhouse/config/flare-users.xml")),
            File.ReadAllText(Path.Combine(RepoRoot(), "deploy/helm/flare/files/flare-users.xml")));
    }

    [Theory]
    [InlineData("db/clickhouse/config/flare-users.xml")]
    [InlineData("db/clickhouse-cluster/config/flare-users.xml")]
    public void UsersFile_TakesEveryPasswordFromTheEnvironment(string path)
    {
        var users = Load(path).Element("users")!.Elements().ToDictionary(u => u.Name.LocalName);

        Assert.Equal("FLARE_CH_INGEST_PASSWORD", PasswordEnv(users["flare_ingest"]));
        Assert.Equal("FLARE_CH_QUERY_PASSWORD", PasswordEnv(users["flare_query"]));
        Assert.Equal("FLARE_CH_APP_PASSWORD", PasswordEnv(users["flare_app"]));
    }

    [Theory]
    [InlineData("db/clickhouse/config/flare-users.xml")]
    [InlineData("db/clickhouse-cluster/config/flare-users.xml")]
    public void QueryUser_IsReadOnlyAndHasNoSourceOrWriteGrants(string path)
    {
        var root = Load(path);
        var queryUser = root.Element("users")!.Element("flare_query")!;
        Assert.Equal("flare_query", queryUser.Element("profile")!.Value);
        Assert.Equal(["GRANT SELECT ON clickhousedb.*"], Grants(root)["flare_query"]);

        // readonly=2 keeps per-query settings working (QuerySafety); 1 would reject them.
        var profile = root.Element("profiles")!.Element("flare_query")!;
        Assert.Equal("2", profile.Element("readonly")!.Value);

        // The ceilings must sit above the Query__* defaults, or every query fails with code 452.
        var limits = new Flare.Api.Query.QueryLimitsOptions();
        var constraints = profile.Element("constraints")!;
        Assert.True(int.Parse(constraints.Element("max_execution_time")!.Element("max")!.Value) >= limits.MaxExecutionSeconds);
        Assert.True(long.Parse(constraints.Element("max_rows_to_read")!.Element("max")!.Value) >= limits.MaxRowsToRead);
    }

    [Theory]
    [InlineData("db/clickhouse/config/flare-users.xml")]
    [InlineData("db/clickhouse-cluster/config/flare-users.xml")]
    public void QueryUser_HasAKeyedQuotaWithNoLimitsByDefault(string path)
    {
        // ADR-0286: keyed by the X-ClickHouse-Quota header Flare.Api sends. Every limit is 0 (track only); admins
        // raise them in a later users.d file. Literal values, not from_env: an unset variable stops ClickHouse.
        var root = Load(path);
        Assert.Equal("flare_query", root.Element("users")!.Element("flare_query")!.Element("quota")!.Value);

        var quota = root.Element("quotas")!.Element("flare_query")!;
        Assert.NotNull(quota.Element("keyed"));
        var interval = quota.Element("interval")!;
        Assert.Equal("3600", interval.Element("duration")!.Value);
        Assert.All(
            interval.Elements().Where(e => e.Name.LocalName != "duration"),
            limit => Assert.Equal("0", limit.Value));
        Assert.DoesNotContain(quota.Descendants(), e => e.Attribute("from_env") is not null);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(" ", false)]
    [InlineData(Ddl, true)]
    public void HasDdlConnection_OnlyWhenClickhousedbIsSet(string? ddl, bool expected)
    {
        // Ingest and Api run migrations only with the DDL credential; without it they wait for the job (ADR-0284).
        var config = Config(("ConnectionStrings:clickhousedb", ddl), ("ConnectionStrings:clickhousedb-query", Query));

        Assert.Equal(expected, FlareClickHouse.HasDdlConnection(config));
    }

    [Theory]
    [InlineData("db/clickhouse/config/flare-users.xml")]
    [InlineData("db/clickhouse-cluster/config/flare-users.xml")]
    public void IngestUser_CanReadMigrationNamesOnly(string path)
    {
        // Ingest's wait for the migration job reads schema_migrations.Name (ADR-0284); flare_app has SELECT on
        // clickhousedb.* already.
        var ingestGrants = Grants(Load(path))["flare_ingest"];

        Assert.Single(ingestGrants, g => g.Contains("schema_migrations", StringComparison.Ordinal));
        Assert.Contains("GRANT SELECT(Name) ON clickhousedb.schema_migrations", ingestGrants);
    }

    private static string? PasswordEnv(XElement user) => user.Element("password")?.Attribute("from_env")?.Value;

    private static Dictionary<string, string[]> Grants(XElement root) =>
        root.Element("users")!.Elements().ToDictionary(
            u => u.Name.LocalName,
            u => u.Element("grants")!.Elements("query").Select(q => q.Value.Trim()).ToArray());

    private static XElement Load(string relativePath) => XElement.Load(Path.Combine(RepoRoot(), relativePath));

    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Flare.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Flare.slnx not found above the test output directory.");
    }
}
