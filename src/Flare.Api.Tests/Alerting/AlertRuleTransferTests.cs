using System.Text.Json;
using Flare.Api.Alerting;
using Flare.Api.Json;
using Flare.Api.Model;
using Xunit;

namespace Flare.Api.Tests.Alerting;

/// <summary>Covers <see cref="AlertRuleTransfer"/> - the pure export/import mapping behind <c>/api/alerts/export|import</c>.</summary>
public class AlertRuleTransferTests
{
    private static readonly Guid ChannelId = Guid.NewGuid();
    private static readonly Guid SloId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 0, 0, 0, TimeSpan.Zero);

    private static AlertRule Rule(string name = "High errors", IReadOnlyList<Guid>? channels = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Condition = new LogFilter(),
        Threshold = new AlertThreshold { Count = 10 },
        WindowSeconds = 300,
        ChannelIds = channels ?? [ChannelId],
        CreatedAt = Now,
        UpdatedAt = Now,
    };

    private static readonly Dictionary<Guid, string> ChannelNames = new() { [ChannelId] = "ops-slack" };
    private static readonly Dictionary<Guid, string> SloNames = new() { [SloId] = "checkout" };

    [Fact]
    public void Export_ReplacesChannelIdsWithNames()
    {
        var item = Assert.Single(AlertRuleTransfer.Export([Rule()], ChannelNames, SloNames).Rules);

        Assert.Equal(["ops-slack"], item.Channels);
        Assert.Null(item.Rule.ChannelIds);
        Assert.False(item.OmittedInlineChannel);
    }

    [Fact]
    public void Export_FlagsInlineChannelAndDropsItsCredentials()
    {
        var rule = Rule(channels: []) with { WebhookUrl = "https://hooks.example/secret" };

        var item = Assert.Single(AlertRuleTransfer.Export([rule], ChannelNames, SloNames).Rules);

        Assert.True(item.OmittedInlineChannel);
        Assert.Null(item.Rule.WebhookUrl);
    }

    [Fact]
    public void RoundTrip_ResolvesNamesBackToIds()
    {
        var export = AlertRuleTransfer.Export([Rule()], ChannelNames, SloNames);

        var (request, error) = AlertRuleTransfer.Resolve(export.Rules[0], AlertRuleTransfer.ByName([(ChannelId, "OPS-slack")]), AlertRuleTransfer.ByName([]));

        Assert.Null(error);
        Assert.Equal([ChannelId], request!.ChannelIds);
    }

    [Fact]
    public void Resolve_UnknownChannel_Errors()
    {
        var export = AlertRuleTransfer.Export([Rule()], ChannelNames, SloNames);

        var (request, error) = AlertRuleTransfer.Resolve(export.Rules[0], AlertRuleTransfer.ByName([]), AlertRuleTransfer.ByName([]));

        Assert.Null(request);
        Assert.Contains("ops-slack", error);
    }

    [Fact]
    public void Resolve_InlineChannelRule_Errors()
    {
        var item = new AlertRuleExportItem { Rule = AlertRuleTransfer.Export([Rule()], ChannelNames, SloNames).Rules[0].Rule, OmittedInlineChannel = true };

        var (request, error) = AlertRuleTransfer.Resolve(item, AlertRuleTransfer.ByName([]), AlertRuleTransfer.ByName([]));

        Assert.Null(request);
        Assert.Contains("inline channel", error);
    }

    [Fact]
    public void SloRule_RemapsSloIdByName()
    {
        var rule = Rule() with
        {
            ConditionKind = AlertConditionKind.SloBurnRate,
            WindowSeconds = 3600,
            SloCondition = new SloBurnRateCondition { SloId = SloId, LongWindowSeconds = 3600, ShortWindowSeconds = 300, BurnRateThreshold = 14.4 },
        };
        var item = Assert.Single(AlertRuleTransfer.Export([rule], ChannelNames, SloNames).Rules);
        Assert.Equal("checkout", item.SloName);

        var newSlo = Guid.NewGuid();
        var (request, error) = AlertRuleTransfer.Resolve(item, AlertRuleTransfer.ByName([(ChannelId, "ops-slack")]), AlertRuleTransfer.ByName([(newSlo, "checkout")]));

        Assert.Null(error);
        Assert.Equal(newSlo, request!.SloCondition!.SloId);

        var (_, missing) = AlertRuleTransfer.Resolve(item, AlertRuleTransfer.ByName([(ChannelId, "ops-slack")]), AlertRuleTransfer.ByName([]));
        Assert.Contains("Unknown SLO", missing);
    }

    [Fact]
    public void Export_SerializesThroughJsonContextAndBack()
    {
        var export = AlertRuleTransfer.Export([Rule()], ChannelNames, SloNames);

        var json = JsonSerializer.Serialize(export, AlertsJsonContext.Default.AlertRulesExport);
        var back = JsonSerializer.Deserialize(json, AlertsJsonContext.Default.AlertRulesExport);

        Assert.Equal("High errors", back!.Rules[0].Rule.Name);
        Assert.Equal(["ops-slack"], back.Rules[0].Channels);
    }
}
