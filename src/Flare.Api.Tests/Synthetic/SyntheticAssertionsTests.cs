using Flare.Api.Model;
using Flare.Api.Synthetic;
using Xunit;

namespace Flare.Api.Tests.Synthetic;

public class SyntheticAssertionsTests
{
    private const string Json = """{"status":"ok","data":{"items":[{"n":3,"ready":true}],"a b":"x"}}""";

    [Theory]
    [InlineData("""status: \w+""", "status: ok", true)]
    [InlineData("""^\d{3}$""", "abc", false)]
    public void Regex_matches_or_fails(string pattern, string body, bool ok) =>
        Assert.Equal(ok, SyntheticAssertions.Error(body, pattern, "", "") is null);

    [Theory]
    [InlineData("$.status", "ok", true)]
    [InlineData("status", "ok", true)]
    [InlineData("$.status", "down", false)]
    [InlineData("$.data.items[0].n", "3", true)]
    [InlineData("$.data.items[0].ready", "true", true)]
    [InlineData("$.data['a b']", "x", true)]
    [InlineData("$.data.items[0].n", "", true)]
    [InlineData("$.data.items[1]", "", false)]
    [InlineData("$.missing", "", false)]
    public void JsonPath_checks_existence_and_value(string path, string expected, bool ok) =>
        Assert.Equal(ok, SyntheticAssertions.Error(Json, "", path, expected) is null);

    [Fact]
    public void JsonPath_on_non_json_body_fails() =>
        Assert.NotNull(SyntheticAssertions.Error("<html>", "", "$.a", ""));

    [Theory]
    [InlineData("(?<=a)b")]
    [InlineData("(a)\\1")]
    [InlineData("(")]
    public void Unsupported_regex_is_rejected(string pattern) => Assert.NotNull(SyntheticAssertions.ValidateRegex(pattern));

    [Theory]
    [InlineData("$.")]
    [InlineData("$[abc]")]
    [InlineData("$.a[")]
    public void Invalid_json_path_is_rejected(string path) => Assert.NotNull(SyntheticAssertions.ValidateJsonPath(path));

    [Fact]
    public void Request_validation_covers_the_new_fields()
    {
        var ok = new SyntheticMonitorRequest { Name = "n", Target = "https://x.test", BodyMatchesRegex = "up", JsonPath = "$.a", JsonPathEquals = "1" };
        Assert.Null(ok.Validate());
        Assert.NotNull((ok with { JsonPath = "" }).Validate());
        Assert.NotNull((ok with { BodyMatchesRegex = "(" }).Validate());
    }
}
