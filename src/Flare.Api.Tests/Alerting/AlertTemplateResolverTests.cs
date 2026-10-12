using Flare.Api.Alerting;
using Flare.Api.Model;
using Xunit;

namespace Flare.Api.Tests.Alerting;

/// <summary>Covers <see cref="AlertTemplateResolver"/> and <see cref="AlertTemplateRequest.Validate"/> (ADR-0148).</summary>
public class AlertTemplateResolverTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 0, 0, 0, TimeSpan.Zero);

    private static AlertRule Rule(Guid? templateId = null, string title = "", string body = "") => new()
    {
        Id = Guid.NewGuid(),
        Name = "r",
        Condition = new LogFilter(),
        Threshold = new AlertThreshold { Count = 1 },
        WindowSeconds = 60,
        CreatedAt = Now,
        UpdatedAt = Now,
        NotificationTemplateId = templateId,
        NotificationTitleTemplate = title,
        NotificationBodyTemplate = body,
    };

    private static AlertTemplate Template(string name = "t", bool isDefault = false, string title = "T", string body = "B", string resolved = "", Dictionary<string, string>? channels = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        IsDefault = isDefault,
        TitleTemplate = title,
        BodyTemplate = body,
        ResolvedBodyTemplate = resolved,
        ChannelBodies = channels ?? [],
        CreatedAt = Now,
        UpdatedAt = Now,
    };

    [Fact]
    public void No_template_and_no_default_leaves_the_rule_untouched()
    {
        var rule = Rule();
        Assert.Same(rule, AlertTemplateResolver.Apply(rule, [Template()], NotificationChannelType.Email, false));
    }

    [Fact]
    public void Referenced_template_fills_empty_title_and_body()
    {
        var t = Template();
        var applied = AlertTemplateResolver.Apply(Rule(t.Id), [t], NotificationChannelType.Email, false);
        Assert.Equal("T", applied.NotificationTitleTemplate);
        Assert.Equal("B", applied.NotificationBodyTemplate);
    }

    [Fact]
    public void Default_applies_when_the_rule_picks_none()
    {
        var d = Template("d", isDefault: true, title: "D", body: "DB");
        var applied = AlertTemplateResolver.Apply(Rule(), [Template(), d], null, false);
        Assert.Equal("D", applied.NotificationTitleTemplate);
        Assert.Equal("DB", applied.NotificationBodyTemplate);
    }

    [Fact]
    public void Referenced_template_beats_the_default()
    {
        var t = Template("t", title: "T");
        var d = Template("d", isDefault: true, title: "D");
        Assert.Equal("T", AlertTemplateResolver.Apply(Rule(t.Id), [d, t], null, false).NotificationTitleTemplate);
    }

    [Fact]
    public void Dangling_reference_falls_back_to_the_default()
    {
        var d = Template("d", isDefault: true, title: "D");
        Assert.Equal("D", AlertTemplateResolver.Apply(Rule(Guid.NewGuid()), [d], null, false).NotificationTitleTemplate);
    }

    [Fact]
    public void Inline_text_overrides_per_field()
    {
        var t = Template();
        var applied = AlertTemplateResolver.Apply(Rule(t.Id, title: "mine"), [t], null, false);
        Assert.Equal("mine", applied.NotificationTitleTemplate);
        Assert.Equal("B", applied.NotificationBodyTemplate);
    }

    [Fact]
    public void Resolved_send_prefers_the_resolved_body_and_falls_back_to_the_fired_one()
    {
        var withResolved = Template(resolved: "OK again");
        Assert.Equal("OK again", AlertTemplateResolver.Apply(Rule(withResolved.Id), [withResolved], NotificationChannelType.Telegram, true).NotificationBodyTemplate);

        var without = Template();
        Assert.Equal("B", AlertTemplateResolver.Apply(Rule(without.Id), [without], NotificationChannelType.Telegram, true).NotificationBodyTemplate);
    }

    [Fact]
    public void Fired_send_uses_the_channel_type_body_when_present()
    {
        var t = Template(channels: new() { ["Telegram"] = "short" });
        Assert.Equal("short", AlertTemplateResolver.Apply(Rule(t.Id), [t], NotificationChannelType.Telegram, false).NotificationBodyTemplate);
        Assert.Equal("B", AlertTemplateResolver.Apply(Rule(t.Id), [t], NotificationChannelType.Email, false).NotificationBodyTemplate);
        Assert.Equal("B", AlertTemplateResolver.Apply(Rule(t.Id), [t], null, false).NotificationBodyTemplate);
    }

    [Fact]
    public void Request_requires_a_name_and_some_text()
    {
        Assert.NotNull(new AlertTemplateRequest { Name = " ", BodyTemplate = "x" }.Validate());
        Assert.NotNull(new AlertTemplateRequest { Name = "n" }.Validate());
        Assert.Null(new AlertTemplateRequest { Name = "n", BodyTemplate = "{{rule_name}}" }.Validate());
    }

    [Fact]
    public void Request_rejects_unknown_placeholders_and_channel_types()
    {
        Assert.Contains("typo", new AlertTemplateRequest { Name = "n", TitleTemplate = "{{typo}}" }.Validate());
        Assert.Contains("channelBodies", new AlertTemplateRequest { Name = "n", ChannelBodies = new Dictionary<string, string> { ["Fax"] = "x" } }.Validate());
        Assert.Contains("nope", new AlertTemplateRequest { Name = "n", ChannelBodies = new Dictionary<string, string> { ["Email"] = "{{nope}}" } }.Validate());
        Assert.Null(new AlertTemplateRequest { Name = "n", ChannelBodies = new Dictionary<string, string> { ["Email"] = "{{message}}" } }.Validate());
    }

    [Fact]
    public void Transfer_round_trips_the_template_by_name()
    {
        var t = Template("ops");
        var channelId = Guid.NewGuid();
        var export = AlertRuleTransfer.Export([Rule(t.Id) with { ChannelIds = [channelId] }], new Dictionary<Guid, string> { [channelId] = "slack" }, new Dictionary<Guid, string>(), new Dictionary<Guid, string> { [t.Id] = "ops" });
        var item = Assert.Single(export.Rules);
        Assert.Equal("ops", item.TemplateName);
        Assert.Null(item.Rule.NotificationTemplateId);

        var (request, error) = AlertRuleTransfer.Resolve(item, AlertRuleTransfer.ByName([(channelId, "slack")]), AlertRuleTransfer.ByName([]), AlertRuleTransfer.ByName([(t.Id, "OPS")]));
        Assert.Null(error);
        Assert.Equal(t.Id, request!.NotificationTemplateId);

        var (_, missing) = AlertRuleTransfer.Resolve(item, AlertRuleTransfer.ByName([(channelId, "slack")]), AlertRuleTransfer.ByName([]));
        Assert.Contains("ops", missing);
    }
}
