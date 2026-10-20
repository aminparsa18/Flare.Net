using Flare.Api.Errors;
using Flare.Api.Model;
using Xunit;

namespace Flare.Api.Tests.Errors;

public class ErrorGroupingTests
{
    private static ErrorGroupingRule Replace(string pattern, string replacement) => new() { Kind = ErrorGroupingKind.Replace, Pattern = pattern, Replacement = replacement };

    private static ErrorGroupingRule Merge(string type, string from, string to) => new() { Kind = ErrorGroupingKind.Merge, ExceptionType = type, Pattern = from, Replacement = to };

    private static ErrorGrouping Grouping(bool normalize = true, params ErrorGroupingRule[] rules) =>
        ErrorGrouping.From(new ErrorGroupingDocument { NormalizeMessages = normalize, Rules = rules });

    [Fact]
    public void Default_IsExactlyTheBuiltInNormalization() =>
        Assert.Equal(ErrorMessageNormalizer.Sql(ErrorGrouping.EventMessage), ErrorGrouping.Default.EventMessageSql());

    [Fact]
    public void NormalizationOff_LeavesTheMessageAlone() =>
        Assert.Equal(ErrorGrouping.EventMessage, Grouping(normalize: false).EventMessageSql());

    [Fact]
    public void Replacements_RunAfterNormalization_InListOrder()
    {
        var sql = Grouping(true, Replace("user [a-z]+", "user <name>"), Replace("tenant-[a-z]+", "tenant")).EventMessageSql();

        var builtIn = sql.IndexOf("'<n>'", StringComparison.Ordinal);
        var first = sql.IndexOf("user <name>", StringComparison.Ordinal);
        var second = sql.IndexOf("'tenant'", StringComparison.Ordinal);
        Assert.True(builtIn > 0 && builtIn < first && first < second);
    }

    [Fact]
    public void Merges_BecomeOneLookupKeyedByTypeAndMessage()
    {
        var sql = Grouping(true, Merge("A.Exception", "old", "new")).EventMessageSql();

        Assert.StartsWith("transform(concat(EventAttributes['exception.type'], char(31), ", sql);
        Assert.Contains(@"['A.Exception\x1fold']", sql);
        Assert.Contains("['new']", sql);
    }

    [Fact]
    public void LiteralsAreEscaped()
    {
        var sql = Grouping(false, Replace(@"it's \d+", "x'\ny")).EventMessageSql();

        Assert.Contains(@"'it\'s \\d+'", sql);
        Assert.Contains(@"'x\'\x0ay'", sql);
    }

    [Fact]
    public void MatchSql_ComparesTheGroupMessageOfTheRowWithThatOfTheParameters()
    {
        var sql = Grouping(true, Merge("T", "a", "b")).MatchSql("exceptionType", "exceptionMessage");

        Assert.Contains("{exceptionType:String}", sql);
        Assert.Contains("{exceptionMessage:String}", sql);
        Assert.Contains(" = transform(concat({exceptionType:String}", sql);
    }

    [Fact]
    public void MergeChains_ResolveToTheFinalTarget()
    {
        var resolved = ErrorGrouping.ResolveMergeChains([("T", "a", "b"), ("T", "b", "c")]);

        Assert.Contains(("T", "a", "c"), resolved);
        Assert.Contains(("T", "b", "c"), resolved);
    }

    [Fact]
    public void MergeChains_AreScopedToTheExceptionType()
    {
        var resolved = ErrorGrouping.ResolveMergeChains([("T", "a", "b"), ("U", "b", "c")]);

        Assert.Contains(("T", "a", "b"), resolved);
    }

    [Fact]
    public void MergeCycles_AreDropped_AndDuplicateSourcesKeepTheFirstRule()
    {
        Assert.Empty(ErrorGrouping.ResolveMergeChains([("T", "a", "b"), ("T", "b", "a")]));
        Assert.Equal([("T", "a", "b")], ErrorGrouping.ResolveMergeChains([("T", "a", "b"), ("T", "a", "c")]));
    }

    [Fact]
    public void Validate_RejectsBadStructure()
    {
        Assert.Null(new ErrorGroupingDocument().Validate());
        Assert.NotNull(new ErrorGroupingDocument { Rules = [Replace("", "x")] }.Validate());
        Assert.NotNull(new ErrorGroupingDocument { Rules = [Merge("", "a", "b")] }.Validate());
        Assert.NotNull(new ErrorGroupingDocument { Rules = [Merge("T", "a", "")] }.Validate());
        Assert.NotNull(new ErrorGroupingDocument { Rules = [Merge("T", "a", "a")] }.Validate());
        Assert.NotNull(new ErrorGroupingDocument { Rules = Enumerable.Repeat(Replace("x", "y"), ErrorGroupingDocument.MaxRules + 1).ToList() }.Validate());
        Assert.Null(new ErrorGroupingDocument { Rules = [Replace("x", "")] }.Validate());
    }

    [Fact]
    public void Sanitized_FillsFieldsAJsonBodyLeftOut()
    {
        // What System.Text.Json produces for {"rules":[{"kind":"Replace","pattern":"x"}]} - omitted init-only properties arrive null.
        var raw = new ErrorGroupingDocument { Rules = [new ErrorGroupingRule { Kind = ErrorGroupingKind.Replace, Pattern = "x", ExceptionType = null!, Replacement = null! }] };

        var clean = raw.Sanitized();

        Assert.Null(clean.Validate());
        Assert.Equal("", clean.Rules[0].ExceptionType);
        Assert.Equal("", clean.Rules[0].Replacement);
        Assert.Empty(new ErrorGroupingDocument { Rules = null! }.Sanitized().Rules);
    }
}
