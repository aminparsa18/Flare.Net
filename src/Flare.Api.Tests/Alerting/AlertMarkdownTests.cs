using Xunit;
using Flare.Api.Alerting;

namespace Flare.Api.Tests.Alerting;

public class AlertMarkdownTests
{
    private static readonly IReadOnlyDictionary<string, string> Values = new Dictionary<string, string>
    {
        ["rule_name"] = "my_*_svc <b>",
        ["rule_url"] = "https://flare.example/alerts?id=1&x=2",
        ["empty_url"] = "",
    };

    private static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string> { ["service.name"] = "checkout" };

    private static string R(string template, AlertMarkupFormat format) => AlertMarkdown.Render(template, Values, Labels, format);

    [Theory]
    [InlineData(AlertMarkupFormat.Plain, "Down: checkout and code")]
    [InlineData(AlertMarkupFormat.TelegramHtml, "<b>Down</b>: <i>checkout</i> and <code>code</code>")]
    [InlineData(AlertMarkupFormat.SlackMrkdwn, "*Down*: _checkout_ and `code`")]
    [InlineData(AlertMarkupFormat.EmailHtml, "<div><strong>Down</strong>: <em>checkout</em> and <code>code</code></div>")]
    public void InlineStyles_RenderPerChannel(AlertMarkupFormat format, string expected) =>
        Assert.Equal(expected, R("**Down**: *{{labels.service.name}}* and `code`", format));

    [Fact]
    public void PlaceholderValues_AreNeverParsedAndAreEscaped()
    {
        Assert.Equal("<i>my_*_svc &lt;b&gt;</i>", R("_{{rule_name}}_", AlertMarkupFormat.TelegramHtml));
        Assert.Equal("my_*_svc <b>", R("{{rule_name}}", AlertMarkupFormat.Plain));
        Assert.Equal("my_*_svc &lt;b&gt;", R("{{rule_name}}", AlertMarkupFormat.SlackMrkdwn));
    }

    [Fact]
    public void UnderscoresInsideWords_AreNotEmphasis() =>
        Assert.Equal("snake_case_word and a_b", R("snake_case_word and a_b", AlertMarkupFormat.TelegramHtml));

    [Fact]
    public void BackslashEscape_KeepsMarkerLiteral() =>
        Assert.Equal("*not italic*", R(@"\*not italic\*", AlertMarkupFormat.Plain));

    [Theory]
    [InlineData(AlertMarkupFormat.Plain, "Open (https://flare.example/alerts?id=1&x=2)")]
    [InlineData(AlertMarkupFormat.TelegramHtml, "<a href=\"https://flare.example/alerts?id=1&amp;x=2\">Open</a>")]
    [InlineData(AlertMarkupFormat.SlackMrkdwn, "<https://flare.example/alerts?id=1&amp;x=2|Open>")]
    public void Link_RendersPerChannel(AlertMarkupFormat format, string expected) =>
        Assert.Equal(expected, R("[Open]({{rule_url}})", format));

    [Fact]
    public void Link_EmptyUrl_CollapsesToLabel() =>
        Assert.Equal("Open", R("[Open]({{empty_url}})", AlertMarkupFormat.TelegramHtml));

    [Fact]
    public void Link_UnsafeScheme_StaysLiteral() =>
        Assert.Equal("[x](javascript:alert(1)", R("[x](javascript:alert(1)", AlertMarkupFormat.Plain));

    [Fact]
    public void Lists_RenderAsBulletsAndNumbers()
    {
        Assert.Equal("• one\n• two\n1. a\n2. b", R("- one\n* two\n1. a\n2) b", AlertMarkupFormat.Plain));
        Assert.Equal("<div><ul><li>one</li><li>two</li></ul><ol><li>a</li></ol></div>", R("- one\n- two\n1. a", AlertMarkupFormat.EmailHtml));
    }

    [Fact]
    public void EmailHtml_NewlinesBecomeBreaks_AndBareUrlsLinkify() =>
        Assert.Equal("<div>a<br>\nsee <a href=\"https://x.io/p\">https://x.io/p</a>.</div>", R("a\nsee https://x.io/p.", AlertMarkupFormat.EmailHtml));

    [Fact]
    public void UnclosedMarkers_StayLiteral() =>
        Assert.Equal("**oops and *x and `y", R("**oops and *x and `y", AlertMarkupFormat.Plain));

    [Fact]
    public void RawHtmlInTemplate_IsEscaped() =>
        Assert.Equal("&lt;script&gt;", R("<script>", AlertMarkupFormat.TelegramHtml));
}
