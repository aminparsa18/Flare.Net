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

    [Theory]
    [InlineData("A ~ B", TraceRelationKind.Sibling, false)]
    [InlineData("A~B", TraceRelationKind.Sibling, false)]
    [InlineData("A !-> B", TraceRelationKind.Child, true)]
    [InlineData("a !=> b", TraceRelationKind.Descendant, true)]
    [InlineData("A !~ B", TraceRelationKind.Sibling, true)]
    [InlineData("A!~B", TraceRelationKind.Sibling, true)]
    [InlineData("A <- B", TraceRelationKind.Parent, false)]
    [InlineData("A<=B", TraceRelationKind.Ancestor, false)]
    [InlineData("a !<- b", TraceRelationKind.Parent, true)]
    [InlineData("A!<=B", TraceRelationKind.Ancestor, true)]
    public void Parse_SiblingAndNegatedRelations(string expression, TraceRelationKind kind, bool negated) =>
        Assert.Equal(new TraceStructureRelation('A', 'B', kind, negated), TraceStructureExpression.Parse(expression));

    [Fact]
    public void Parse_ANegatedRelationIsNotTheNotOfARelation()
    {
        // `!->` is one operator; `! A -> B` is NOT applied to the whole relation.
        Assert.Equal(new TraceStructureRelation('A', 'B', TraceRelationKind.Child, true), TraceStructureExpression.Parse("A !-> B"));
        Assert.Equal(new TraceStructureNot(new TraceStructureRelation('A', 'B', TraceRelationKind.Child)), TraceStructureExpression.Parse("! A -> B"));
    }

    [Theory]
    [InlineData("A ~ B ~ C")]
    [InlineData("A !~ B -> C")]
    [InlineData("~ B")]
    [InlineData("A !->")]
    public void Parse_RejectsAChainOrAMissingOperand(string expression) =>
        Assert.Throws<ArgumentException>(() => TraceStructureExpression.Parse(expression));

    [Theory]
    [InlineData(TraceRelationKind.Child, false, "->")]
    [InlineData(TraceRelationKind.Descendant, false, "=>")]
    [InlineData(TraceRelationKind.Sibling, false, "~")]
    [InlineData(TraceRelationKind.Child, true, "!->")]
    [InlineData(TraceRelationKind.Descendant, true, "!=>")]
    [InlineData(TraceRelationKind.Sibling, true, "!~")]
    [InlineData(TraceRelationKind.Parent, false, "<-")]
    [InlineData(TraceRelationKind.Ancestor, false, "<=")]
    [InlineData(TraceRelationKind.Parent, true, "!<-")]
    [InlineData(TraceRelationKind.Ancestor, true, "!<=")]
    public void Operator_WritesWhatParseReads(TraceRelationKind kind, bool negated, string expected)
    {
        Assert.Equal(expected, TraceStructureExpression.Operator(kind, negated));
        Assert.Equal(new TraceStructureRelation('A', 'B', kind, negated), TraceStructureExpression.Parse($"A {expected} B"));
    }

    [Theory]
    [InlineData(TraceRelationKind.Child, 'A', 'B')]
    [InlineData(TraceRelationKind.Descendant, 'A', 'B')]
    [InlineData(TraceRelationKind.Parent, 'B', 'A')]
    [InlineData(TraceRelationKind.Ancestor, 'B', 'A')]
    public void UpperAndLower_NameTheHigherAndTheLowerSideWhicheverWayTheOperatorReads(TraceRelationKind kind, char upper, char lower)
    {
        var relation = new TraceStructureRelation('A', 'B', kind);

        Assert.Equal((upper, lower), (relation.Upper, relation.Lower));
        Assert.Equal(kind is TraceRelationKind.Parent or TraceRelationKind.Ancestor, relation.Reversed);
    }

    [Fact]
    public void Parse_RejectsAChainOfTurnedRoundRelations() =>
        Assert.Throws<ArgumentException>(() => TraceStructureExpression.Parse("A <- B <= C"));
}
