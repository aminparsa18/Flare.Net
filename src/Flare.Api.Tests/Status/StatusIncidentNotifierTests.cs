using Flare.Api.Alerting;
using Flare.Api.Model;
using Flare.Api.Status;
using Xunit;

namespace Flare.Api.Tests.Status;

public class StatusIncidentNotifierTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private static readonly StatusPage Page = new() { Id = Guid.NewGuid(), Slug = "acme", Title = "Acme", CreatedAt = Now, UpdatedAt = Now };

    private static StatusIncident Incident(string title = "API down") => new()
    {
        Id = Guid.NewGuid(), PageId = Page.Id, Title = title, CreatedAt = Now, UpdatedAt = Now,
    };

    [Fact]
    public void BuildRule_TitleCarriesPageStatusAndIncident_BodyCarriesMessageComponentsAndLink()
    {
        var update = new StatusIncidentUpdate(Now, StatusIncidentStatus.Identified, "Found the bad deploy.");

        var rule = StatusIncidentNotifier.BuildRule(Page, Incident(), update, ["API", "Checkout"], "https://flare.example.com/");

        Assert.Equal("[Acme] Identified: API down", rule.NotificationTitleTemplate);
        Assert.Equal("Found the bad deploy.\n\nAffected: API, Checkout\n\nhttps://flare.example.com/status/acme", rule.NotificationBodyTemplate);
    }

    [Fact]
    public void BuildRule_OmitsAffectedAndLink_WhenThereAreNone()
    {
        var update = new StatusIncidentUpdate(Now, StatusIncidentStatus.Resolved, "Fixed.");

        var rule = StatusIncidentNotifier.BuildRule(Page, Incident(), update, [], "");

        Assert.Equal("Fixed.", rule.NotificationBodyTemplate);
    }

    [Fact]
    public void BuildRule_DefusesPlaceholders_SoAdminTextIsNeverSubstituted()
    {
        var update = new StatusIncidentUpdate(Now, StatusIncidentStatus.Investigating, "see {{value}}");

        var rule = StatusIncidentNotifier.BuildRule(Page, Incident("{{rule}}"), update, [], null);
        var message = AlertMessageFormatter.BuildMessage(rule, 0, false, null, null, Now, false, null);

        Assert.Equal("see { {value}}", message.Text);
        Assert.Contains("{ {rule}}", message.Title);
    }

    [Theory]
    [InlineData(NotificationChannelType.Webhook, true)]
    [InlineData(NotificationChannelType.Telegram, true)]
    [InlineData(NotificationChannelType.Email, true)]
    [InlineData(NotificationChannelType.Teams, true)]
    [InlineData(NotificationChannelType.Discord, true)]
    [InlineData(NotificationChannelType.PagerDuty, false)]
    [InlineData(NotificationChannelType.Jira, false)]
    public void IsSupported_AllowsOnlyAnnouncementChannels(NotificationChannelType type, bool expected) =>
        Assert.Equal(expected, StatusIncidentNotifier.IsSupported(type));

    [Fact]
    public void Request_RejectsTooManySubscribers()
    {
        var request = new StatusPageRequest
        {
            Slug = "acme", Title = "Acme",
            SubscriberChannelIds = Enumerable.Range(0, StatusPageRequest.MaxSubscribers + 1).Select(_ => Guid.NewGuid()).ToList(),
        };

        Assert.Contains("subscribed channels", request.Validate());
    }
}
