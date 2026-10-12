using Flare.Api.Erasure;
using Xunit;

namespace Flare.Api.Tests.Erasure;

public class ErasureSqlTests
{
    private static ErasureSql.ErasureTable Table(string name) => ErasureSql.Tables.Single(t => t.Name == name);

    private static ErasureIdentifier Id(string key, string value) => new() { Key = key, Value = value };

    [Fact]
    public void Literal_EscapesBackslashAndQuote()
    {
        Assert.Equal(@"'a\\b\'c'", ErasureSql.Literal(@"a\b'c"));
    }

    [Fact]
    public void BuildPredicate_Logs_MatchesEveryAttributeMap()
    {
        Assert.Equal(
            "ResourceAttributes['user.id'] = 'u1' OR ScopeAttributes['user.id'] = 'u1' OR LogAttributes['user.id'] = 'u1'",
            ErasureSql.BuildPredicate(Table("logs"), [Id("user.id", "u1")], []));
    }

    [Fact]
    public void BuildPredicate_Spans_IncludesEventAttributes()
    {
        var sql = ErasureSql.BuildPredicate(Table("spans"), [Id("user.email", "a@b.c")], []);
        Assert.Contains("SpanAttributes['user.email'] = 'a@b.c'", sql);
        Assert.Contains("arrayExists(m -> m['user.email'] = 'a@b.c', `Events.Attributes`)", sql);
    }

    [Fact]
    public void BuildPredicate_MultipleIdentifiers_AreOred()
    {
        var sql = ErasureSql.BuildPredicate(Table("profile_samples"), [Id("user.id", "u1"), Id("client.address", "10.0.0.1")], []);
        Assert.Equal(
            "ResourceAttributes['user.id'] = 'u1' OR SampleAttributes['user.id'] = 'u1' OR ResourceAttributes['client.address'] = '10.0.0.1' OR SampleAttributes['client.address'] = '10.0.0.1'",
            sql);
    }

    [Fact]
    public void BuildPredicate_Feedback_MatchesPlainColumnsAndSessions()
    {
        var sql = ErasureSql.BuildPredicate(Table("app_feedback"), [Id("user.id", "u1"), Id("user.email", "a@b.c")], ["s1", "s'2"]);
        Assert.Equal(@"UserId = 'u1' OR Email = 'a@b.c' OR SessionId IN ('s1', 's\'2')", sql);
    }

    [Fact]
    public void BuildPredicate_Feedback_IgnoresUnrelatedKeys()
    {
        Assert.Equal("0", ErasureSql.BuildPredicate(Table("app_feedback"), [Id("client.address", "10.0.0.1")], []));
    }

    [Fact]
    public void BuildPredicate_Screenshots_NeedSessions()
    {
        Assert.Equal("0", ErasureSql.BuildPredicate(Table("app_screenshots"), [Id("user.id", "u1")], []));
        Assert.Equal("SessionId IN ('s1')", ErasureSql.BuildPredicate(Table("app_screenshots"), [Id("user.id", "u1")], ["s1"]));
    }

    [Fact]
    public void BuildPredicate_ValueWithQuoteCannotBreakOut()
    {
        var sql = ErasureSql.BuildPredicate(Table("logs"), [Id("user.id", "x' OR 1=1 --")], []);
        Assert.Contains(@"= 'x\' OR 1=1 --'", sql);
    }

    [Fact]
    public void BuildDelete_SingleNode_TargetsTableAndEmbedsTransactionId()
    {
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");
        Assert.Equal(
            "ALTER TABLE clickhousedb.logs DELETE WHERE (ResourceAttributes['user.id'] = 'u1' OR ScopeAttributes['user.id'] = 'u1' OR LogAttributes['user.id'] = 'u1') AND '11111111-2222-3333-4444-555555555555' != ''",
            ErasureSql.BuildDelete(Table("logs"), [Id("user.id", "u1")], [], id, clusterMode: false));
    }

    [Fact]
    public void BuildDelete_ClusterMode_TargetsLocalTableOnCluster()
    {
        var sql = ErasureSql.BuildDelete(Table("spans"), [Id("user.id", "u1")], [], Guid.Empty, clusterMode: true);
        Assert.StartsWith("ALTER TABLE clickhousedb.spans_local ON CLUSTER 'flare_cluster' DELETE WHERE (", sql);
    }

    [Fact]
    public void BuildMutationProgressSql_ClusterMode_ReadsAllReplicas()
    {
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");
        Assert.Contains("FROM system.mutations", ErasureSql.BuildMutationProgressSql(id, clusterMode: false));
        Assert.Contains("clusterAllReplicas('flare_cluster', system.mutations)", ErasureSql.BuildMutationProgressSql(id, clusterMode: true));
        Assert.Contains("position(command, '11111111-2222-3333-4444-555555555555')", ErasureSql.BuildMutationProgressSql(id, clusterMode: false));
    }

    [Fact]
    public void BuildSessionIdsSql_ReadsTheSessionAttributeOfTheTable()
    {
        Assert.Contains("SpanAttributes['session.id']", ErasureSql.BuildSessionIdsSql(Table("spans"), [Id("user.id", "u1")]));
        Assert.Contains("LogAttributes['session.id']", ErasureSql.BuildSessionIdsSql(Table("logs"), [Id("user.id", "u1")]));
    }

    [Fact]
    public void BuildExportSql_UsesFormatRowAndLimit()
    {
        Assert.StartsWith("SELECT formatRow('JSONEachRow', *) FROM logs WHERE ", ErasureSql.BuildExportSql(Table("logs"), [Id("user.id", "u1")], [], 5));
        Assert.EndsWith("LIMIT 5", ErasureSql.BuildExportSql(Table("logs"), [Id("user.id", "u1")], [], 5));
    }

    [Theory]
    [InlineData("", "v")]
    [InlineData("user.id", "")]
    [InlineData("user.id", "a\nb")]
    public void ValidateIdentifier_RejectsEmptyAndControlCharacters(string key, string value)
    {
        Assert.NotNull(ErasureSql.ValidateIdentifier(key, value));
    }

    [Fact]
    public void SubjectHash_IsOrderIndependentAndDoesNotContainTheValue()
    {
        var a = ErasureSql.SubjectHash([Id("user.id", "u1"), Id("user.email", "a@b.c")]);
        var b = ErasureSql.SubjectHash([Id("user.email", "a@b.c"), Id("user.id", "u1")]);
        Assert.Equal(a, b);
        Assert.Equal(16, a.Length);
        Assert.NotEqual(a, ErasureSql.SubjectHash([Id("user.id", "u2")]));
    }

    [Fact]
    public void Validate_RequiresIdentifiersAndConfirmation()
    {
        Assert.NotNull(new ErasureRequest().Validate(requireConfirm: false));
        var request = new ErasureRequest { Identifiers = [Id("user.id", "u1")], Confirm = "nope" };
        Assert.Null(request.Validate(requireConfirm: false));
        Assert.NotNull(request.Validate(requireConfirm: true));
        Assert.Null((request with { Confirm = "u1" }).Validate(requireConfirm: true));
    }

    [Fact]
    public void ProgressOf_ReflectsMutationState()
    {
        var none = TimeSpan.FromSeconds(5);
        Assert.Equal(ErasureStatus.Running, ErasureService.ProgressOf("logs", 3, ErasureOperationStatus.Submitted, null, none).Status);
        Assert.Equal(ErasureStatus.Success, ErasureService.ProgressOf("logs", 3, ErasureOperationStatus.Submitted, null, TimeSpan.FromMinutes(10)).Status);
        Assert.Equal(ErasureStatus.Running, ErasureService.ProgressOf("logs", 3, ErasureOperationStatus.Submitted, new ErasureService.MutationState(2, 1, 4, ""), none).Status);
        Assert.Equal(ErasureStatus.Success, ErasureService.ProgressOf("logs", 3, ErasureOperationStatus.Submitted, new ErasureService.MutationState(2, 2, 0, ""), none).Status);
        var failed = ErasureService.ProgressOf("logs", 3, ErasureOperationStatus.Submitted, new ErasureService.MutationState(1, 0, 4, "boom"), none);
        Assert.Equal(ErasureStatus.Failed, failed.Status);
        Assert.Equal("boom", failed.Error);
    }
}
