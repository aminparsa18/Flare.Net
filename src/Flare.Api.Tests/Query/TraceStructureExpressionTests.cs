using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class TraceStructureExpressionTests
{
    [Fact]
    public void Parse_BareLetter_IsHas()
    {
        Assert.Equal(new TraceStructureHas('A'), TraceStructureExpression.Parse("A"));
    }

    [Theory]
    [InlineData("A -> B", true)]
    [InlineData("A->B", true)]
    [InlineData("a => b", false)]
    public void Parse_Relation_ParentChildAndKind(string expression, bool direct)
    {
        Assert.Equal(new TraceStructureRelation('A', 'B', direct), TraceStructureExpression.Parse(expression));
    }

    [Fact]
    public void Parse_RelationBindsTighterThanNotAndAndTighterThanOr()
    {
        var parsed = TraceStructureExpression.Parse("NOT A -> B AND C OR D => E");

        var expected = new TraceStructureOr(
            new TraceStructureAnd(new TraceStructureNot(new TraceStructureRelation('A', 'B', true)), new TraceStructureHas('C')),
            new TraceStructureRelation('D', 'E', false));
        Assert.Equal(expected, parsed);
    }

    [Fact]
    public void Parse_SymbolOperatorsAndParentheses()
    {
        var parsed = TraceStructureExpression.Parse("!(A || B) && C");

        var expected = new TraceStructureAnd(
            new TraceStructureNot(new TraceStructureOr(new TraceStructureHas('A'), new TraceStructureHas('B'))),
            new TraceStructureHas('C'));
        Assert.Equal(expected, parsed);
    }

    [Fact]
    public void Parse_KeywordsAreCaseInsensitive()
    {
        Assert.Equal(TraceStructureExpression.Parse("A AND NOT B"), TraceStructureExpression.Parse("a and not b"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A ->")]
    [InlineData("-> B")]
    [InlineData("A -> B -> C")]
    [InlineData("A => (B)")]
    [InlineData("(A")]
    [InlineData("A B")]
    [InlineData("A AND")]
    [InlineData("AB")]
    [InlineData("A = B")]
    [InlineData("A )")]
    public void Parse_RejectsMalformedExpressions(string expression)
    {
        Assert.Throws<ArgumentException>(() => TraceStructureExpression.Parse(expression));
    }

    [Fact]
    public void Parse_RejectsChains_WithAHint()
    {
        var ex = Assert.Throws<ArgumentException>(() => TraceStructureExpression.Parse("A -> B => C"));

        Assert.Contains("AND", ex.Message);
    }

    [Fact]
    public void Parse_RejectsOverlongExpressions()
    {
        Assert.Throws<ArgumentException>(() => TraceStructureExpression.Parse(string.Join(" OR ", Enumerable.Repeat("A", 200))));
    }

    [Fact]
    public void Letters_CollectsEveryConditionOnce()
    {
        var letters = TraceStructureExpression.Letters(TraceStructureExpression.Parse("(A -> B OR C) AND NOT (A => D)"));

        Assert.Equal(['A', 'B', 'C', 'D'], letters);
    }
}
