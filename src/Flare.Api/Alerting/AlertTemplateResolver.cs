using Flare.Api.Model;

namespace Flare.Api.Alerting;

/// <summary>
/// Picks the wording a rule actually sends (ADR-0148). Pure and free of I/O so it is
/// unit-tested directly; <see cref="CompositeAlertNotifier"/> applies it at send time, which
/// is why <see cref="AlertMessageFormatter"/> and every notifier stay unaware of shared templates.
/// </summary>
public static class AlertTemplateResolver
{
    /// <summary>
    /// The template whose wording applies to <paramref name="rule"/>: the one it references, else
    /// the instance default, else none. A reference to a template that no longer exists falls
    /// through to the default rather than failing a notification.
    /// </summary>
    public static AlertTemplate? Select(AlertRule rule, IReadOnlyList<AlertTemplate> templates)
    {
        if (rule.NotificationTemplateId is { } id && templates.FirstOrDefault(t => t.Id == id) is { } picked)
        {
            return picked;
        }

        return templates.FirstOrDefault(t => t.IsDefault);
    }

    /// <summary>
    /// <paramref name="rule"/> with its empty title/body filled from the selected template, which
    /// is how <see cref="AlertMessageFormatter.BuildMessage"/> sees it. The rule's own inline
    /// text always wins, per field. For the body, a resolved send prefers
    /// <see cref="AlertTemplate.ResolvedBodyTemplate"/>; otherwise the channel type's entry in
    /// <see cref="AlertTemplate.ChannelBodies"/>; otherwise <see cref="AlertTemplate.BodyTemplate"/>.
    /// </summary>
    /// <param name="channelType">Null for the preview, which has no channel, so only the general bodies apply.</param>
    public static AlertRule Apply(AlertRule rule, IReadOnlyList<AlertTemplate> templates, NotificationChannelType? channelType, bool resolved)
    {
        if (Select(rule, templates) is not { } template)
        {
            return rule;
        }

        var title = string.IsNullOrEmpty(rule.NotificationTitleTemplate) ? template.TitleTemplate : rule.NotificationTitleTemplate;
        var body = string.IsNullOrEmpty(rule.NotificationBodyTemplate) ? PickBody(template, channelType, resolved) : rule.NotificationBodyTemplate;
        return rule with { NotificationTitleTemplate = title, NotificationBodyTemplate = body };
    }

    private static string PickBody(AlertTemplate template, NotificationChannelType? channelType, bool resolved)
    {
        if (resolved && !string.IsNullOrEmpty(template.ResolvedBodyTemplate))
        {
            return template.ResolvedBodyTemplate;
        }

        if (!resolved && channelType is { } type && template.ChannelBodies.TryGetValue(type.ToString(), out var channelBody) && !string.IsNullOrEmpty(channelBody))
        {
            return channelBody;
        }

        return template.BodyTemplate;
    }
}
