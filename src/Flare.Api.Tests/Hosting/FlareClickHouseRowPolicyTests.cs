using System.Text.RegularExpressions;
using System.Xml.Linq;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Hosting;

/// <summary>
/// The opt-in row policies on <c>flare_query</c> (ADR-0287). Whether they filter is only provable against a
/// real server; these keep the three copies in step and make a new service-keyed table fail here rather than
/// leak past the policies.
/// </summary>
public partial class FlareClickHouseRowPolicyTests
{
    private const string SingleNode = "db/clickhouse/config/flare-row-policies.xml";
    private const string Cluster = "db/clickhouse-cluster/config/flare-row-policies.xml";

    /// <summary>Service-keyed tables that are config, scoped by ProjectId rather than by service (ADR-0123).</summary>
    private static readonly string[] NotCovered = ["slos"];

    [Fact]
    public void EveryServiceKeyedTableInTheMigrations_HasAPolicy()
    {
        var keyed = ServiceKeyedTables().Except(NotCovered).Order();

        Assert.Equal(keyed, Policies(SingleNode).Keys.Order());
    }

    [Fact]
    public void ClusterFile_PutsTheSamePoliciesOnTheLocalTables()
    {
        // Row policies don't apply on a Distributed table, so the cluster copy filters each shard's _local table.
        var single = Policies(SingleNode);
        var cluster = Policies(Cluster);

        Assert.Equal(single.Keys.Select(t => t + "_local").Order(), cluster.Keys.Order());
        Assert.All(single, p => Assert.Equal(p.Value, cluster[p.Key + "_local"]));
        Assert.Equal(ProfileDefault(SingleNode), ProfileDefault(Cluster));
    }

    [Fact]
    public void HelmChartCopy_MatchesTheSingleNodeFile()
    {
        Assert.Equal(
            File.ReadAllText(Path.Combine(RepoRoot(), SingleNode)),
            File.ReadAllText(Path.Combine(RepoRoot(), "deploy/helm/flare/files/flare-row-policies.xml")));
    }

    [Fact]
    public void EveryPolicy_IsTheSameFilterOnItsServiceColumn()
    {
        var policies = Policies(SingleNode);
        var template = policies["logs"];

        Assert.Contains($"getSetting('{ServiceScope.RowPolicySettingName}')", template, StringComparison.Ordinal);
        Assert.All(policies, p => Assert.Equal(
            p.Key == "service_dependency_nodes" ? template.Replace("ServiceName", "Service", StringComparison.Ordinal) : template,
            p.Value));
    }

    [Theory]
    [InlineData(SingleNode)]
    [InlineData(Cluster)]
    public void ProfileDefault_FailsClosed_AsAQuotedLiteral(string path)
    {
        // '[]' matches no row. ClickHouse parses profile settings as literals: a bare [] stops it from starting.
        Assert.Equal("'[]'", ProfileDefault(path));
    }

    private static Dictionary<string, string> Policies(string path) =>
        Load(path).Element("users")!.Element("flare_query")!.Element("databases")!.Element("clickhousedb")!.Elements()
            .ToDictionary(t => t.Name.LocalName, t => t.Element("filter")!.Value);

    private static string ProfileDefault(string path) =>
        Load(path).Element("profiles")!.Element("flare_query")!.Element(ServiceScope.RowPolicySettingName)!.Value;

    /// <summary>Tables a single-node migration creates, or alters, with a ServiceName or Service column.</summary>
    private static HashSet<string> ServiceKeyedTables()
    {
        var tables = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(Path.Combine(RepoRoot(), "db/clickhouse"), "*.sql"))
        {
            var sql = LineComment().Replace(File.ReadAllText(file), string.Empty);
            foreach (var statement in sql.Split(';'))
            {
                if (CreateTable().Match(statement) is { Success: true } create && ServiceColumn().IsMatch(statement))
                {
                    tables.Add(create.Groups[1].Value);
                }

                if (AddServiceColumn().Match(statement) is { Success: true } alter)
                {
                    tables.Add(alter.Groups[1].Value);
                }
            }
        }

        return tables;
    }

    [GeneratedRegex("--[^\n]*")]
    private static partial Regex LineComment();

    [GeneratedRegex(@"CREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?(?:clickhousedb\.)?`?(\w+)", RegexOptions.IgnoreCase)]
    private static partial Regex CreateTable();

    [GeneratedRegex(@"^\s*(?:ServiceName|Service)\s+\w", RegexOptions.Multiline)]
    private static partial Regex ServiceColumn();

    [GeneratedRegex(@"ALTER\s+TABLE\s+(?:clickhousedb\.)?(\w+)[\s\S]*ADD\s+COLUMN\s+(?:IF\s+NOT\s+EXISTS\s+)?(?:ServiceName|Service)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AddServiceColumn();

    private static XElement Load(string relativePath) => XElement.Load(Path.Combine(RepoRoot(), relativePath));

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
