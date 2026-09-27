using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class TraceStructureSqlBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    private static readonly TraceSpanCondition Checkout = new() { Name = "A", ServiceName = "checkout" };
    private static readonly TraceSpanCondition Payment = new() { Name = "B", ServiceName = "payment" };
    private static readonly TraceSpanCondition PaymentError = new() { Name = "C", ServiceName = "payment", StatusCode = "STATUS_CODE_ERROR" };

    private static TraceStructureFilter Structure(string expression, params TraceSpanCondition[] conditions) =>
        new() { Conditions = conditions, Expression = expression };

    private static (string Sql, ClickHouseParameterCollection Parameters) Build(TraceStructureFilter structure)
    {
        var parameters = new ClickHouseParameterCollection();
        return (TraceStructureSqlBuilder.BuildTraceIdQuery(structure, parameters, null), parameters);
    }

    [Fact]
    public void DirectChild_OnePassOverMatchingSpans_HasAnyOfParentIdsAgainstChildParents()
    {
        var (sql, parameters) = Build(Structure("A -> B", Checkout, Payment));

        Assert.Equal(
            "SELECT TraceId FROM (SELECT TraceId, " +
            "countIf((ServiceName = {tsAService:String})) > 0 AS hA, " +
            "groupArrayIf(cityHash64(SpanId), (ServiceName = {tsAService:String})) AS sA, " +
            "countIf((ServiceName = {tsBService:String})) > 0 AS hB, " +
            "groupArrayIf(if(ParentSpanId = '', toUInt64(0), cityHash64(ParentSpanId)), (ServiceName = {tsBService:String})) AS pB " +
            "FROM spans WHERE StartTime >= {from:DateTime64(9)} AND StartTime < {to:DateTime64(9)} " +
            "AND ServiceName IN {tsServices:Array(String)} " +
            "AND ((ServiceName = {tsAService:String}) OR (ServiceName = {tsBService:String})) " +
            "GROUP BY TraceId) WHERE hasAny(sA, pB)",
            sql);
        Assert.Equal("checkout", parameters["tsAService"].Value);
        Assert.Equal(new[] { "checkout", "payment" }, parameters["tsServices"].Value);
    }

    [Fact]
    public void Descendant_TwoStages_CandidatesThenExactWalkOverWholeTraces()
    {
        var (sql, _) = Build(Structure("A => B", Checkout, Payment));

        const string stage1 = "SELECT TraceId FROM (SELECT TraceId, countIf((ServiceName = {tsAService:String})) > 0 AS hA";
        Assert.StartsWith("SELECT TraceId FROM (SELECT TraceId, countIf(", sql);
        Assert.Contains($"TraceId GLOBAL IN ({stage1}", sql);
        Assert.Contains("GROUP BY TraceId) WHERE (hA AND hB))", sql);
        Assert.Contains("groupArray(cityHash64(SpanId)) AS allS, groupArray(if(ParentSpanId = '', toUInt64(0), cityHash64(ParentSpanId))) AS allP", sql);
        Assert.EndsWith(
            "WHERE (hasAny(sA, pB) OR arrayFold((acc, x) -> (arrayMap(h -> if(h = 0, toUInt64(0), allP[indexOf(allS, h)]), acc.1), acc.2 OR hasAny(acc.1, sA)), " +
            "range(least(64, length(allS))), (pB, false)).2)",
            sql);

        // Stage 2 reads every span of the candidate traces - no service prefilter there.
        var outerFrom = sql.IndexOf("FROM spans WHERE", StringComparison.Ordinal);
        var outerWhere = sql[outerFrom..sql.IndexOf("GLOBAL IN", outerFrom, StringComparison.Ordinal)];
        Assert.Equal("FROM spans WHERE StartTime >= {from:DateTime64(9)} AND StartTime < {to:DateTime64(9)} AND TraceId ", outerWhere);
    }

    [Fact]
    public void NegatedDescendant_Stage1UsesTheDirectChildLowerBound()
    {
        var (sql, _) = Build(Structure("A AND NOT A => C", Checkout, PaymentError));

        Assert.Contains("GROUP BY TraceId) WHERE (hA AND NOT hasAny(sA, pC)))", sql);
        Assert.EndsWith("(pC, false)).2))", sql);
    }

    [Fact]
    public void Condition_AllFieldsAndAttributes_AreParameterizedPerLetter()
    {
        var condition = new TraceSpanCondition
        {
            Name = "b",
            SpanName = "charge",
            StatusCode = "STATUS_CODE_ERROR",
            MinDurationNano = 5_000_000,
            Attributes = [new SpanAttributeFilter { Key = "payment.provider", Value = "stripe" }],
        };

        var (sql, parameters) = Build(Structure("B", condition));

        Assert.Contains(
            "countIf((Name = {tsBName:String} AND StatusCode = {tsBStatus:String} AND DurationNano >= {tsBMinDuration:UInt64} AND SpanAttributes[{attrKeyTB_0:String}] = {attrValueTB_0:String})) > 0 AS hB",
            sql);
        Assert.DoesNotContain("tsServices", sql);
        Assert.EndsWith("WHERE hB", sql);
        Assert.Equal("stripe", parameters["attrValueTB_0"].Value);
        Assert.Equal(5_000_000UL, parameters["tsBMinDuration"].Value);
    }

    [Fact]
    public void UnreferencedConditions_AreLeftOut()
    {
        var (sql, _) = Build(Structure("A", Checkout, Payment));

        Assert.DoesNotContain("tsBService", sql);
    }

    [Fact]
    public void Validate_IgnoresABlankConditionTheExpressionDoesntUse()
    {
        var (_, conditions) = TraceStructureSqlBuilder.Validate(Structure("A", Checkout, new TraceSpanCondition { Name = "B" }));

        Assert.Equal(['A'], conditions.Keys);
    }

    [Theory]
    [InlineData("NOT A")]
    [InlineData("A OR NOT B")]
    [InlineData("NOT (A -> B)")]
    public void Validate_RejectsExpressionsThatMatchTracesWithNoConditionSpans(string expression)
    {
        var ex = Assert.Throws<ArgumentException>(() => TraceStructureSqlBuilder.Validate(Structure(expression, Checkout, Payment)));

        Assert.Contains("none of its spans", ex.Message);
    }

    [Fact]
    public void Validate_RejectsBadConditions()
    {
        Assert.Throws<ArgumentException>(() => TraceStructureSqlBuilder.Validate(new TraceStructureFilter { Expression = "A" }));
        Assert.Throws<ArgumentException>(() => TraceStructureSqlBuilder.Validate(Structure("A", Enumerable.Range(0, 7).Select(i => Checkout with { Name = ((char)('A' + i)).ToString() }).ToArray())));
        Assert.Contains("single letter", Assert.Throws<ArgumentException>(() => TraceStructureSqlBuilder.Validate(Structure("A", Checkout with { Name = "AB" }))).Message);
        Assert.Contains("twice", Assert.Throws<ArgumentException>(() => TraceStructureSqlBuilder.Validate(Structure("A", Checkout, Payment with { Name = "a" }))).Message);
        Assert.Contains("needs a service", Assert.Throws<ArgumentException>(() => TraceStructureSqlBuilder.Validate(Structure("A", new TraceSpanCondition { Name = "A" }))).Message);
        Assert.Contains("unknown status", Assert.Throws<ArgumentException>(() => TraceStructureSqlBuilder.Validate(Structure("A", Checkout with { StatusCode = "ERROR" }))).Message);
        Assert.Contains("isn't defined", Assert.Throws<ArgumentException>(() => TraceStructureSqlBuilder.Validate(Structure("A -> D", Checkout))).Message);
    }

    [Fact]
    public void SpanFilter_Structure_RestrictsToMatchingTraceIds_OverTheFilterWindow()
    {
        var filter = new SpanFilter { RootSpansOnly = true, Structure = Structure("A -> B", Checkout, Payment) };

        var result = SpanFilterSqlBuilder.Build(filter, Now);

        Assert.Contains("ParentSpanId = '' AND TraceId GLOBAL IN (SELECT TraceId FROM (SELECT TraceId, countIf(", result.WhereSql);
        Assert.Equal(Now.AddHours(-1).UtcDateTime, result.Parameters["from"].Value);
    }
}
