using System.Text.RegularExpressions;
using Flare.Api.Loki;
using Xunit;

namespace Flare.Api.Tests.Loki;

public class LokiLogfmtTests
{
    [Fact]
    public void Parse_BareAndQuotedValues()
    {
        var pairs = LokiLogfmt.Parse("""level=info msg="user logged in" user_id=42 duration=1.5s""");

        Assert.Equal(
            [("level", "info"), ("msg", "user logged in"), ("user_id", "42"), ("duration", "1.5s")],
            pairs);
    }

    [Fact]
    public void Parse_QuotedValuesUnescapeQuotesAndBackslashes()
    {
        var pairs = LokiLogfmt.Parse("""msg="say \"hi\" to C:\\temp" next=ok""");

        Assert.Equal([("msg", """say "hi" to C:\temp"""), ("next", "ok")], pairs);
    }

    [Fact]
    public void Parse_KeysAreSanitisedToLabelNames()
    {
        var pairs = LokiLogfmt.Parse("http.method=GET x-request-id=abc");

        Assert.Equal([("http_method", "GET"), ("x_request_id", "abc")], pairs);
    }

    [Fact]
    public void Parse_SkipsKeysWithoutValuesAndEmptyValues()
    {
        var pairs = LokiLogfmt.Parse("""standalone empty= quoted_empty="" ok=1""");

        Assert.Equal([("ok", "1")], pairs);
    }

    [Fact]
    public void Parse_ToleratesTabsAndRepeatedSpaces()
    {
        Assert.Equal([("a", "1"), ("b", "2")], LokiLogfmt.Parse("a=1 \t  b=2"));
    }

    [Theory]
    [InlineData("""{"level":"info","msg":"a b=c"}""")]
    [InlineData("""   {"x": "y z=1"}""")]
    public void Parse_AJsonObjectIsNotLogfmt(string body)
    {
        Assert.Empty(LokiLogfmt.Parse(body));
    }

    [Fact]
    public void Parse_PlainTextWithoutPairsHasNoLabels()
    {
        Assert.Empty(LokiLogfmt.Parse("GET /health 200 in 3ms"));
    }

    [Fact]
    public void Sql_DefinesTheMapFromTheSamePatternTheCSideUses()
    {
        var sql = LokiLogfmt.MapSql;

        Assert.Contains("AS lkv", sql);
        Assert.Contains("extractAllGroups(", sql);
        // Backslashes of the regex are doubled for the ClickHouse string literal.
        Assert.Contains(LokiSqlLiteral.Of(LokiLogfmt.PairPattern), sql);
        Assert.Contains("startsWith(trimLeft(Body), '{')", sql);
    }

    [Fact]
    public void PairPattern_IsPlainRe2Syntax()
    {
        // No lookarounds, backreferences or shorthand classes whose meaning differs between RE2 and .NET.
        Assert.DoesNotContain("(?<", LokiLogfmt.PairPattern);
        Assert.DoesNotContain("\\s", LokiLogfmt.PairPattern);
        _ = new Regex(LokiLogfmt.PairPattern, RegexOptions.NonBacktracking);
    }
}

public class LokiPatternMatcherTests
{
    private static bool Matches(string pattern, string line) =>
        new Regex(LokiPatternMatcher.ToRegex(pattern), RegexOptions.NonBacktracking).IsMatch(line);

    [Fact]
    public void LeadingLiteralAnchorsTheStartOfTheLine()
    {
        Assert.True(Matches("GET <_> 200", "GET /x 200 12ms"));
        Assert.False(Matches("GET <_> 200", "request: GET /x 200"));
    }

    [Fact]
    public void LeadingCaptureMatchesAnywhere()
    {
        Assert.True(Matches("<_> failed to connect <_>", "2026-10-16 12:00 failed to connect to db"));
        Assert.False(Matches("<_> failed to connect <_>", "2026-10-16 12:00 connected"));
    }

    [Fact]
    public void LiteralsMustAppearInOrder()
    {
        Assert.True(Matches("<_>a<_>b<_>", "xx a yy b zz"));
        Assert.False(Matches("<_>a<_>b<_>", "xx b yy a zz"));
    }

    [Fact]
    public void RegexMetacharactersInLiteralsAreEscaped()
    {
        Assert.True(Matches("<_> (took 5ms) [ok] <_>", "x (took 5ms) [ok] y"));
        Assert.False(Matches("<_> a.b <_>", "x aXb y"));
        Assert.True(Matches("<_> a+b|c <_>", "x a+b|c y"));
    }

    [Fact]
    public void CapturesSpanNewlines()
    {
        Assert.True(Matches("<_> stack <_>", "line one\nline two stack trace\nmore"));
    }

    [Theory]
    [InlineData("", "non-empty")]
    [InlineData("<name> x", "named captures")]
    [InlineData("<_><_>", "two captures")]
    public void Validate_RejectsWhatLokiRejects(string pattern, string mention)
    {
        var ex = Assert.Throws<LokiException>(() => LokiPatternMatcher.Validate(pattern));

        Assert.Contains(mention, ex.Message);
    }

    [Fact]
    public void AngleBracketsThatAreNotCapturesAreLiteral()
    {
        Assert.True(Matches("a < b > c <_>", "a < b > c done"));
    }
}

public class LokiJsonPathTests
{
    private static string Describe(string path) =>
        string.Join("|", LokiJsonPath.Parse(path).Select(s => s.Key ?? "#" + s.Index));

    [Theory]
    [InlineData("status", "status")]
    [InlineData("request.method", "request|method")]
    [InlineData("""["request"]["method"]""", "request|method")]
    [InlineData("""request["x.y"]""", "request|x.y")]
    [InlineData("servers[0].name", "servers|#0|name")]
    [InlineData("""["a"][12]["b"]""", "a|#12|b")]
    [InlineData("""["with \"quote"]""", "with \"quote")]
    [InlineData("", "")]
    public void Parse_AcceptsDotsBracketsAndIndexes(string path, string expected)
    {
        Assert.Equal(expected, Describe(path));
    }

    [Theory]
    [InlineData("a[")]
    [InlineData("a[x]")]
    [InlineData("""a["b]""")]
    [InlineData("""a["b"x""")]
    public void Parse_RejectsMalformedPaths(string path)
    {
        Assert.Throws<LokiException>(() => LokiJsonPath.Parse(path));
    }
}

public class LokiLogfmtStrictTests
{
    // ---- --strict (ADR-0238) ----

    [Fact]
    public void Strict_AWellFormedLineHasNoFault()
    {
        var (pairs, failed) = LokiLogfmt.ParseStrict("""level=info msg="user \"x\" in" bare standalone= n=42""");

        Assert.False(failed);
        Assert.Equal([("level", "info"), ("msg", "user \"x\" in"), ("n", "42")], pairs);
    }

    [Theory]
    [InlineData("a=1 b=c=d e=2", "a=1")]
    [InlineData("a=1 b=\"unterminated c=2", "a=1")]
    [InlineData("a=1 =oops", "a=1")]
    [InlineData("a=1 bad\"key=2", "a=1")]
    [InlineData("a=1 b=x\"y", "a=1")]
    [InlineData("a=1 b=\"x\\qy\"", "a=1")]
    [InlineData("a=\"1\"\"2\"", "a=1")]
    public void Strict_StopsAtTheFirstFaultAndKeepsTheLabelsBeforeIt(string line, string expectedPrefix)
    {
        var (pairs, failed) = LokiLogfmt.ParseStrict(line);

        Assert.True(failed);
        Assert.Equal(LokiLogfmt.Parse(expectedPrefix), pairs);
    }

    [Fact]
    public void Strict_AJsonLineIsAFaultWithNoLabels()
    {
        var (pairs, failed) = LokiLogfmt.ParseStrict("""{"level":"info"}""");

        Assert.True(failed);
        Assert.Empty(pairs);
    }

    [Fact]
    public void Strict_AQuotedValueMayBeFollowedDirectlyByAnotherKey()
    {
        var (_, failed) = LokiLogfmt.ParseStrict("""a="1"b=2""");

        Assert.False(failed);
    }

    [Fact]
    public void StrictSql_ReadsLabelsFromThePrefixAndFlagsTheWholeLine()
    {
        var sql = LokiLogfmt.StrictMapSql;

        Assert.Contains(LokiSqlLiteral.Of(LokiLogfmt.StrictPrefixPattern), sql);
        Assert.Contains(LokiSqlLiteral.Of(LokiLogfmt.StrictValidPattern), sql);
        Assert.Contains("AS lkok", sql);
        Assert.Contains("AS lkv", sql);
        Assert.DoesNotContain("startsWith(trimLeft(Body)", sql);
    }

    [Fact]
    public void StrictPatterns_ArePlainRe2Syntax()
    {
        foreach (var pattern in new[] { LokiLogfmt.StrictItemPattern, LokiLogfmt.StrictPrefixPattern, LokiLogfmt.StrictValidPattern })
        {
            Assert.DoesNotContain("(?<", pattern);
            Assert.DoesNotContain("\\s", pattern);
        }
    }
}
