using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class ServiceCallBreakdownQueryBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_ExternalCallsQuery_GroupsByExternalTarget_FilteredToRequestedService()
    {
        var result = ServiceCallBreakdownQueryBuilder.Build("checkout-api", TimeSpan.FromMinutes(15), Now);

        Assert.Contains($"{ServiceCallBreakdownQueryBuilder.ExternalTargetExpr} AS PeerService", result.ExternalCallsSql);
        Assert.Contains("count() AS CallCount", result.ExternalCallsSql);
        Assert.Contains("countIf(StatusCode = {errorStatus:String}) AS ErrorCount", result.ExternalCallsSql);
        Assert.Contains("quantile(0.5)(DurationNano) AS P50DurationNano", result.ExternalCallsSql);
        Assert.Contains("quantile(0.95)(DurationNano) AS P95DurationNano", result.ExternalCallsSql);
        Assert.Contains($"WHERE ServiceName = {{service:String}} AND {ServiceCallBreakdownQueryBuilder.ExternalTargetExpr} != ''", result.ExternalCallsSql);
        Assert.Contains("GROUP BY PeerService", result.ExternalCallsSql);
        Assert.Contains("ORDER BY CallCount DESC", result.ExternalCallsSql);

        var parameters = result.ExternalCallsParameters.ToDictionary();
        Assert.Equal("checkout-api", parameters["service"]);
    }

    [Fact]
    public void ExternalTargetExpr_PrefersPeerService_ThenFallsBackToTheDomainOfOutboundClientCallsOnly()
    {
        var expr = ServiceCallBreakdownQueryBuilder.ExternalTargetExpr;

        Assert.StartsWith("if(SpanAttributes['peer.service'] != '', SpanAttributes['peer.service'], ", expr);
        Assert.Contains($"if({ExternalApiQueryBuilder.OutboundCallCondition}, {ExternalApiQueryBuilder.DomainExpr}, '')", expr);
        Assert.Contains("Kind = 3", ExternalApiQueryBuilder.OutboundCallCondition);
        Assert.Contains("SpanAttributes['db.system.name'] = ''", ExternalApiQueryBuilder.OutboundCallCondition);
        Assert.Contains("SpanAttributes['db.system'] = ''", ExternalApiQueryBuilder.OutboundCallCondition);
        Assert.Contains("SpanAttributes['messaging.system'] = ''", ExternalApiQueryBuilder.OutboundCallCondition);
    }

    [Theory]
    [InlineData("Flare.ServiceDefaults.ClickHouseMigrations.Sql.0037_db_stable_semconv.sql")]
    [InlineData("Flare.ServiceDefaults.ClickHouseMigrations.SqlCluster.0037_db_stable_semconv.sql")]
    public void Migration0037_KeysTheBreakdownMaterializedViewsByTheSameExpressionsAsTheLiveQuery(string resourceName)
    {
        // The pre-aggregated and live paths must group identically, or toggling
        // ServiceDependencyMetrics (or adding a filter chip) would change the rows. 0037 is
        // the latest definition of both views (0023 created them, 0034 re-keyed the external one).
        var sql = ReadMigration(resourceName);

        Assert.Contains($"{ServiceCallBreakdownQueryBuilder.ExternalTargetExpr} AS PeerService", sql);
        Assert.Contains($"{ServiceCallBreakdownQueryBuilder.DbSystemExpr} AS DbSystem", sql);
        Assert.Contains($"{ServiceCallBreakdownQueryBuilder.DbOperationExpr} AS DbOperation", sql);
        Assert.Contains("WHERE DbSystem != ''", sql);
    }

    [Fact]
    public void DbSystemAndOperationExprs_PreferTheStableAttributes_ThenTheOlderOnes()
    {
        Assert.StartsWith("if(SpanAttributes['db.system.name'] != '', SpanAttributes['db.system.name'], SpanAttributes['db.system'])", ServiceCallBreakdownQueryBuilder.DbSystemExpr);

        var op = ServiceCallBreakdownQueryBuilder.DbOperationExpr;
        Assert.True(op.IndexOf("'db.operation.name'", StringComparison.Ordinal) < op.IndexOf("'db.operation'", StringComparison.Ordinal));
        Assert.True(op.IndexOf("'db.operation'", StringComparison.Ordinal) < op.IndexOf("'db.query.text'", StringComparison.Ordinal));
        Assert.Contains("SpanAttributes['db.statement']", op);
        // No backslashes: they'd need escaping once for C# and again for the SQL string literal.
        Assert.DoesNotContain("\\", op);
    }

    private static string ReadMigration(string resourceName)
    {
        var assembly = typeof(Flare.ServiceDefaults.ClickHouseMigrations.ClickHouseMigrationRunner).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName);
        Assert.NotNull(stream);
        return new StreamReader(stream).ReadToEnd();
    }

    [Fact]
    public void Build_DatabaseCallsQuery_GroupsByDbSystemAndOperation_FilteredToRequestedService()
    {
        var result = ServiceCallBreakdownQueryBuilder.Build("checkout-api", TimeSpan.FromMinutes(15), Now);

        Assert.Contains($"{ServiceCallBreakdownQueryBuilder.DbSystemExpr} AS DbSystem", result.DatabaseCallsSql);
        Assert.Contains($"{ServiceCallBreakdownQueryBuilder.DbOperationExpr} AS DbOperation", result.DatabaseCallsSql);
        Assert.Contains($"WHERE ServiceName = {{service:String}} AND {ServiceCallBreakdownQueryBuilder.DbSystemExpr} != ''", result.DatabaseCallsSql);
        Assert.Contains("GROUP BY DbSystem, DbOperation", result.DatabaseCallsSql);
        Assert.Contains("ORDER BY CallCount DESC", result.DatabaseCallsSql);

        var parameters = result.DatabaseCallsParameters.ToDictionary();
        Assert.Equal("checkout-api", parameters["service"]);
    }

    [Fact]
    public void Build_BindsFromAsNowMinusWindow_AndToAsNow_ForBothQueries()
    {
        var result = ServiceCallBreakdownQueryBuilder.Build("checkout-api", TimeSpan.FromMinutes(15), Now);

        var externalParameters = result.ExternalCallsParameters.ToDictionary();
        Assert.Equal(Now.AddMinutes(-15).UtcDateTime, externalParameters["from"]);
        Assert.Equal(Now.UtcDateTime, externalParameters["to"]);
        Assert.Equal("STATUS_CODE_ERROR", externalParameters["errorStatus"]);

        var databaseParameters = result.DatabaseCallsParameters.ToDictionary();
        Assert.Equal(Now.AddMinutes(-15).UtcDateTime, databaseParameters["from"]);
        Assert.Equal(Now.UtcDateTime, databaseParameters["to"]);
    }

    [Theory]
    [InlineData(0, ServiceCallBreakdownQueryBuilder.DefaultWindowMinutes)]
    [InlineData(-5, ServiceCallBreakdownQueryBuilder.DefaultWindowMinutes)]
    [InlineData(30, 30)]
    [InlineData(5000, ServiceCallBreakdownQueryBuilder.MaxWindowMinutes)]
    public void ClampWindowMinutes_DefaultsAndClamps_SameAsServiceOverviewQueryBuilder(int requested, int expected)
    {
        Assert.Equal(expected, ServiceCallBreakdownQueryBuilder.ClampWindowMinutes(requested));
    }

    [Fact]
    public void Build_WithResourceAttributes_AndsInEqualityClauses_OnBothQueries()
    {
        var resourceAttributes = new[] { new ResourceAttributeFilter { Key = "deployment.environment", Value = "production" } };

        var result = ServiceCallBreakdownQueryBuilder.Build("checkout-api", TimeSpan.FromMinutes(15), Now, resourceAttributes);

        Assert.Contains("ResourceAttributes[{ResAttrKey0:String}] = {ResAttrValue0:String}", result.ExternalCallsSql);
        Assert.Contains("ResourceAttributes[{ResAttrKey0:String}] = {ResAttrValue0:String}", result.DatabaseCallsSql);

        var externalParameters = result.ExternalCallsParameters.ToDictionary();
        Assert.Equal("deployment.environment", externalParameters["ResAttrKey0"]);
        Assert.Equal("production", externalParameters["ResAttrValue0"]);

        var databaseParameters = result.DatabaseCallsParameters.ToDictionary();
        Assert.Equal("deployment.environment", databaseParameters["ResAttrKey0"]);
        Assert.Equal("production", databaseParameters["ResAttrValue0"]);
    }
}
