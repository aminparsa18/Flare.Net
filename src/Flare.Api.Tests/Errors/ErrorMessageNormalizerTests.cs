using System.Text.RegularExpressions;
using Flare.Api.Errors;
using Xunit;

namespace Flare.Api.Tests.Errors;

public class ErrorMessageNormalizerTests
{
    // The rules run in ClickHouse (RE2); .NET's engine accepts the same patterns, so the tests apply
    // them here to pin the behaviour. The SQL itself is verified against a real server by hand.
    private static string Normalize(string message)
    {
        foreach (var (pattern, replacement) in ErrorMessageNormalizer.Rules)
        {
            message = Regex.Replace(message, pattern, replacement.Replace(@"\1", "$1"), RegexOptions.ECMAScript);
        }

        return message;
    }

    [Theory]
    [InlineData("Order 12345 not found", "Order <n> not found")]
    [InlineData("Timeout after 30.5 seconds", "Timeout after <n>.<n> seconds")]
    [InlineData("User 3f2504e0-4f89-11d3-9a0c-0305e82c3301 missing", "User <guid> missing")]
    [InlineData("Bad pointer 0xDEADBEEF", "Bad pointer <hex>")]
    [InlineData("Key 'abc' not found", "Key '<s>' not found")]
    [InlineData("Column \"first name\" is invalid", "Column \"<s>\" is invalid")]
    [InlineData("failed at 2026-10-20T10:15:00Z", "failed at <ts>")]
    [InlineData("at 2026-10-20 10:15:00.123+02:00 and 2026-10-21T01:02:03Z", "at <ts> and <ts>")]
    [InlineData("no variable parts here", "no variable parts here")]
    [InlineData("", "")]
    public void Normalizes_VariableParts(string input, string expected) =>
        Assert.Equal(expected, Normalize(input));

    [Fact]
    public void KeepsIdentifiersWithEmbeddedDigits() =>
        Assert.Equal("HTTP2 stream sha256 mismatch", Normalize("HTTP2 stream sha256 mismatch"));

    [Fact]
    public void ApostropheInProse_IsNotTreatedAsAQuote() =>
        Assert.Equal("Can't open file, it's gone", Normalize("Can't open file, it's gone"));

    [Fact]
    public void DifferentIdsOfTheSameError_ShareOneGroup() =>
        Assert.Equal(Normalize("Order 1 not found for customer 'a'"), Normalize("Order 987 not found for customer 'zed'"));

    [Theory]
    [InlineData("Order 12345 not found for 'x'")]
    [InlineData("id 3f2504e0-4f89-11d3-9a0c-0305e82c3301 at 0xFF")]
    public void IsIdempotent_SoAGroupMessageMatchesItself(string input)
    {
        var once = Normalize(input);
        Assert.Equal(once, Normalize(once));
    }

    [Fact]
    public void Sql_NestsEveryRule_AndEscapesBackslashesAndQuotes()
    {
        var sql = ErrorMessageNormalizer.Sql("m");

        Assert.Equal(ErrorMessageNormalizer.Rules.Count, Regex.Matches(sql, "replaceRegexpAll").Count);
        Assert.StartsWith("replaceRegexpAll(", sql);
        Assert.Contains(@"'\\b[0-9]+\\b'", sql);
        Assert.Contains(@"'\\1\'<s>\''", sql);
    }

    [Fact]
    public void MatchSql_NormalizesBothSides() =>
        Assert.Equal($"{ErrorMessageNormalizer.Sql("m")} = {ErrorMessageNormalizer.Sql("{p:String}")}", ErrorMessageNormalizer.MatchSql("m", "p"));

    [Fact]
    public void Migration0087_UsesTheSameExpression()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "Flare.slnx")))
        {
            root = Path.GetDirectoryName(root);
        }

        Assert.NotNull(root);
        foreach (var folder in new[] { "clickhouse", "clickhouse-cluster" })
        {
            var migration = File.ReadAllText(Path.Combine(root!, "db", folder, "0087_error_message_normalization.sql"));
            Assert.Contains(ErrorMessageNormalizer.Sql("ExceptionMessage"), migration);
        }
    }
}
