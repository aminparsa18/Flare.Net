using Flare.Api.Model;

namespace Flare.Api.Alerting;

/// <summary>
/// Pure mapping between saved <see cref="AlertRule"/>s and the portable <see cref="AlertRulesExport"/>
/// document: ids out and names in on export, names resolved back to ids (and the request
/// validated) on import. No I/O, so it is unit-tested directly.
/// </summary>
public static class AlertRuleTransfer
{
    public static AlertRulesExport Export(
        IEnumerable<AlertRule> rules,
        IReadOnlyDictionary<Guid, string> channelNames,
        IReadOnlyDictionary<Guid, string> sloNames,
        IReadOnlyDictionary<Guid, string>? templateNames = null) => new()
        {
            Rules = [.. rules.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).Select(r => ExportRule(r, channelNames, sloNames, templateNames ?? new Dictionary<Guid, string>()))],
        };

    private static AlertRuleExportItem ExportRule(AlertRule rule, IReadOnlyDictionary<Guid, string> channelNames, IReadOnlyDictionary<Guid, string> sloNames, IReadOnlyDictionary<Guid, string> templateNames)
    {
        var hasInline = rule.ChannelIds.Count == 0
            && (rule.WebhookUrl.Length > 0 || rule.TelegramBotToken.Length > 0 || rule.EmailTo.Length > 0 || rule.PagerDutyRoutingKey.Length > 0);

        string? sloName = null;
        if (rule.SloCondition is { } slo)
        {
            sloNames.TryGetValue(slo.SloId, out sloName);
        }

        return new AlertRuleExportItem
        {
            Rule = new AlertRuleRequest
            {
                Name = rule.Name,
                Description = rule.Description,
                Enabled = rule.Enabled,
                Condition = rule.Condition,
                Threshold = rule.Threshold,
                WindowSeconds = rule.WindowSeconds,
                CooldownSeconds = rule.CooldownSeconds,
                ConditionKind = rule.ConditionKind,
                MetricCondition = rule.MetricCondition,
                MetricThresholdValue = rule.MetricThresholdValue,
                ExceptionCondition = rule.ExceptionCondition,
                NoDataWindowSeconds = rule.NoDataWindowSeconds,
                EvaluationIntervalSeconds = rule.EvaluationIntervalSeconds,
                AnomalyCondition = rule.AnomalyCondition,
                MinDataPoints = rule.MinDataPoints,
                NotificationTitleTemplate = rule.NotificationTitleTemplate,
                NotificationBodyTemplate = rule.NotificationBodyTemplate,
                RecoveryThreshold = rule.RecoveryThreshold,
                Severity = rule.Severity,
                ThresholdUnit = rule.ThresholdUnit,
                Labels = rule.Labels,
                SloCondition = rule.SloCondition is { } s ? s with { SloId = Guid.Empty } : null,
                NotificationTemplateId = null,
            },
            TemplateName = rule.NotificationTemplateId is { } templateId && templateNames.TryGetValue(templateId, out var templateName) ? templateName : null,
            Channels = [.. rule.ChannelIds.Select(id => channelNames.TryGetValue(id, out var name) ? name : id.ToString())],
            SloName = sloName,
            OmittedInlineChannel = hasInline,
        };
    }

    /// <summary>
    /// Resolves <paramref name="item"/>'s channel/SLO names against the target instance. Returns the
    /// create request, or an error message when a reference is missing or the rule is invalid.
    /// </summary>
    public static (AlertRuleRequest? Request, string? Error) Resolve(
        AlertRuleExportItem item,
        IReadOnlyDictionary<string, Guid> channelIds,
        IReadOnlyDictionary<string, Guid> sloIds,
        IReadOnlyDictionary<string, Guid>? templateIds = null)
    {
        if (item.Rule is null)
        {
            return (null, "Entry has no rule.");
        }

        if (string.IsNullOrWhiteSpace(item.Rule.Name))
        {
            return (null, "Rule name is required.");
        }

        if (item.OmittedInlineChannel)
        {
            return (null, "The source rule used an inline channel whose credentials are never exported - recreate it with a notification channel and re-export.");
        }

        var ids = new List<Guid>();
        var missing = new List<string>();
        foreach (var name in item.Channels ?? [])
        {
            if (channelIds.TryGetValue(name, out var id))
            {
                ids.Add(id);
            }
            else
            {
                missing.Add(name);
            }
        }

        if (missing.Count > 0)
        {
            return (null, $"Unknown notification channel(s): {string.Join(", ", missing)}.");
        }

        var request = item.Rule with
        {
            ChannelIds = ids,
            WebhookUrl = null,
            TelegramBotToken = null,
            TelegramChatId = null,
            EmailTo = null,
            PagerDutyRoutingKey = null,
        };

        if (request.SloCondition is { } slo)
        {
            if (string.IsNullOrEmpty(item.SloName) || !sloIds.TryGetValue(item.SloName, out var sloId))
            {
                return (null, $"Unknown SLO '{item.SloName}'.");
            }

            request = request with { SloCondition = slo with { SloId = sloId } };
        }

        if (!string.IsNullOrEmpty(item.TemplateName))
        {
            if (templateIds is null || !templateIds.TryGetValue(item.TemplateName, out var templateId))
            {
                return (null, $"Unknown notification template '{item.TemplateName}'.");
            }

            request = request with { NotificationTemplateId = templateId };
        }

        var error = request.ValidateChannel() ?? request.ValidateCondition();
        return error is null ? (request, null) : (null, error);
    }

    /// <summary>Case-insensitive name lookup that keeps the first of any duplicates.</summary>
    public static Dictionary<string, Guid> ByName(IEnumerable<(Guid Id, string Name)> entries)
    {
        var map = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, name) in entries)
        {
            map.TryAdd(name, id);
        }

        return map;
    }
}
