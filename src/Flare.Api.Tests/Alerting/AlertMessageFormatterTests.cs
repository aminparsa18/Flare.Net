using Flare.Api.Alerting;
using Flare.Api.Model;
using Xunit;

namespace Flare.Api.Tests.Alerting;

/// <summary>
/// Covers <see cref="AlertMessageFormatter"/>'s deep-link behavior - null/blank
/// <c>publicUrl</c> omits the link entirely (today's behavior, unchanged for anyone who
/// hasn't set <see cref="AlertLinkOptions.PublicUrl"/>), a configured one produces
/// <c>{publicUrl}/alerts?rule={id}</c> with a trailing slash trimmed, appended as the last
/// line of <see cref="AlertMessageFormatter.BuildText"/>'s text - see that method's own
/// remarks for why a bare URL rather than channel-specific link markup.
/// </summary>
public class AlertMessageFormatterTests
{
    private static AlertRule MakeRule(string name = "High error rate") => new()
    {
        Id = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        Name = name,
        Condition = new LogFilter(),
        Threshold = new AlertThreshold { Count = 10 },
        WindowSeconds = 60,
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };

    [Fact]
    public void BuildRuleUrl_NullPublicUrl_ReturnsNull()
    {
        Assert.Null(AlertMessageFormatter.BuildRuleUrl(MakeRule(), null));
    }

    [Fact]
    public void BuildRuleUrl_BlankPublicUrl_ReturnsNull()
    {
        Assert.Null(AlertMessageFormatter.BuildRuleUrl(MakeRule(), "   "));
    }

    [Fact]
    public void BuildRuleUrl_ConfiguredPublicUrl_BuildsDeepLink()
    {
        var url = AlertMessageFormatter.BuildRuleUrl(MakeRule(), "https://flare.example.com");

        Assert.Equal("https://flare.example.com/alerts?rule=11111111-2222-3333-4444-555555555555", url);
    }

    [Fact]
    public void BuildRuleUrl_TrailingSlashOnPublicUrl_IsTrimmed()
    {
        var url = AlertMessageFormatter.BuildRuleUrl(MakeRule(), "https://flare.example.com/");

        Assert.Equal("https://flare.example.com/alerts?rule=11111111-2222-3333-4444-555555555555", url);
    }

    [Fact]
    public void BuildText_NoPublicUrl_HasNoLink()
    {
        var text = AlertMessageFormatter.BuildText(MakeRule(), observedCount: 42);

        Assert.DoesNotContain("http", text, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildText_WithPublicUrl_AppendsLinkOnItsOwnLine()
    {
        var text = AlertMessageFormatter.BuildText(MakeRule(), observedCount: 42, publicUrl: "https://flare.example.com");

        var lines = text.Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Equal("https://flare.example.com/alerts?rule=11111111-2222-3333-4444-555555555555", lines[1]);
        Assert.Contains("High error rate", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void BuildText_TestNotification_StillAppendsLink()
    {
        var text = AlertMessageFormatter.BuildText(MakeRule(), observedCount: 0, isTest: true, publicUrl: "https://flare.example.com");

        Assert.Contains("Test notification", text, StringComparison.Ordinal);
        Assert.EndsWith("https://flare.example.com/alerts?rule=11111111-2222-3333-4444-555555555555", text, StringComparison.Ordinal);
    }
}
