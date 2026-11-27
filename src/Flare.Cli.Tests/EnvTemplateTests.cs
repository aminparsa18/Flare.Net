using System.Text.RegularExpressions;
using Flare.Cli.Internal;
using Xunit;

namespace Flare.Cli.Tests;

/// <summary>
/// A fresh instance's .env must fill every placeholder, the ClickHouse user passwords (ADR-0282) included:
/// an empty FLARE_CH_*_PASSWORD would give that ClickHouse user an empty password.
/// </summary>
public class EnvTemplateTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RenderedEnv_FillsEveryPlaceholder_WithDistinctClickHousePasswords(bool cluster)
    {
        var profile = TopologyProfile.For(cluster ? FlareTopology.Cluster : FlareTopology.Standalone);
        using var stream = typeof(EnvGenerator).Assembly.GetManifestResourceStream(profile.EnvTemplateResourceName)!;
        var template = new StreamReader(stream).ReadToEnd();

        var env = EnvGenerator.RenderEnvTemplate(template, profile.Ports);

        Assert.DoesNotContain("{{", env);
        var passwords = new[] { "CLICKHOUSE_PASSWORD", "FLARE_CH_INGEST_PASSWORD", "FLARE_CH_QUERY_PASSWORD", "FLARE_CH_APP_PASSWORD" }
            .Select(key => Regex.Match(env, $"(?m)^{key}=(\\S+)$").Groups[1].Value)
            .ToArray();
        Assert.All(passwords, p => Assert.Matches("^[A-Za-z0-9]{32}$", p));
        Assert.Equal(passwords.Length, passwords.Distinct().Count());
    }

    [Fact]
    public void StandaloneUsersFile_IsEmbedded()
    {
        Assert.Contains(
            "Flare.Cli.Templates.ClickHouseConfig.flare-users.xml",
            typeof(EnvGenerator).Assembly.GetManifestResourceNames());
    }
}
