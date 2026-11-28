using System.Text.RegularExpressions;
using Flare.Cli.Internal;
using Xunit;

namespace Flare.Cli.Tests;

/// <summary>
/// ADR-0285: an instance whose compose file runs the clickhouse-migrate job can't run an image without the
/// `migrate` command (releases before 0.7.0). Non-release tags (edge, latest, sha-…) always pass, and a compose
/// file from an older CLI, which migrates at startup, accepts any tag.
/// </summary>
public class ImageTagPolicyTests
{
    [Theory]
    [InlineData("0.6.0", true)]
    [InlineData("0.6", true)]
    [InlineData("v0.5.1", true)]
    [InlineData("0.5.1-rc1", true)]
    [InlineData("0.7.0", false)]
    [InlineData("0.7", false)]
    [InlineData("0.7.0-rc1", false)]
    [InlineData("0.10.0", false)]
    [InlineData("1.0.0", false)]
    [InlineData("edge", false)]
    [InlineData("latest", false)]
    [InlineData("sha-1a2b3c4", false)]
    [InlineData("", false)]
    public void IsOlderThanMigrationJob(string tag, bool expected) =>
        Assert.Equal(expected, ImageTagPolicy.IsOlderThanMigrationJob(tag));

    [Theory]
    [InlineData(true, "0.6.0", true)]
    [InlineData(true, "0.7.0", false)]
    [InlineData(true, "edge", false)]
    [InlineData(false, "0.6.0", false)]
    public void Validate_RefusesOldTagsOnlyForJobModeComposeFiles(bool jobMode, string tag, bool refused)
    {
        var directory = Directory.CreateTempSubdirectory("flare-cli-tag-").FullName;
        try
        {
            var instance = new FlareInstance("t", directory, "flare-t");
            File.WriteAllText(instance.ComposeFilePath, jobMode
                ? "services:\n  clickhouse-migrate:\n    command: [\"migrate\"]\n  api:\n"
                : "services:\n  api:\n");

            var error = ImageTagPolicy.Validate(instance, tag);

            Assert.Equal(refused, error is not null);
            if (refused)
            {
                Assert.Contains("flare update -n t --tag 0.7.0", error);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComposeTemplates_GiveClickHouseDbOnlyToTheMigrationJob(bool cluster)
    {
        var profile = TopologyProfile.For(cluster ? FlareTopology.Cluster : FlareTopology.Standalone);
        using var stream = typeof(EnvGenerator).Assembly.GetManifestResourceStream(profile.ComposeResourceName)!;
        var compose = new StreamReader(stream).ReadToEnd();

        Assert.Matches(@"(?m)^  clickhouse-migrate:\s*$", compose);
        // Split into top-level service blocks and find every one holding the `default` DDL credential.
        var holders = Regex.Split(compose, @"(?m)^(?=  [a-z0-9-]+:\s*$)")
            .Where(block => block.Contains("ConnectionStrings__clickhousedb:"))
            .Select(block => Regex.Match(block, @"^  ([a-z0-9-]+):").Groups[1].Value)
            .ToArray();
        Assert.Equal(["clickhouse-migrate"], holders);
    }
}
