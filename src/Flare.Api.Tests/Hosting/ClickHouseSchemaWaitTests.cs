using Flare.ServiceDefaults.ClickHouseMigrations;
using Xunit;

namespace Flare.Api.Tests.Hosting;

/// <summary>
/// The pure half of waiting for the migration job (ADR-0284): which migrations a service expects, and which
/// are still missing. The polling itself needs a real ClickHouse.
/// </summary>
public class ClickHouseSchemaWaitTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmbeddedMigrationNames_AreTheSqlFilesInOrder(bool clusterMode)
    {
        var names = ClickHouseMigrationRunner.EmbeddedMigrationNames(clusterMode);

        Assert.NotEmpty(names);
        Assert.StartsWith("0001_", names[0], StringComparison.Ordinal);
        Assert.All(names, n => Assert.EndsWith(".sql", n, StringComparison.Ordinal));
        Assert.Equal(names.Order(StringComparer.Ordinal), names);
    }

    [Fact]
    public void MissingMigrations_KeepsOrder_AndIgnoresExtraApplied()
    {
        // An extra applied row is a newer job ahead of this service, which is fine.
        var missing = ClickHouseMigrationRunner.MissingMigrations(
            ["0001_a.sql", "0002_b.sql", "0003_c.sql"],
            new HashSet<string>(["0002_b.sql", "0099_newer.sql"]));

        Assert.Equal(["0001_a.sql", "0003_c.sql"], missing);
    }
}
