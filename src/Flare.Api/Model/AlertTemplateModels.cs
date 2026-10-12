namespace Flare.Api.Model;

/// <summary>
/// A named, reusable set of ADR-0052 notification texts. A rule picks one by
/// <see cref="AlertRule.NotificationTemplateId"/>; the one flagged <see cref="IsDefault"/>
/// applies to rules that pick none. See
/// <c>docs-internal/adr/0148-shared-alert-notification-templates.md</c>.
/// </summary>
public sealed record AlertTemplate
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = "";

    /// <summary>True for the instance-wide default. At most one template has it.</summary>
    public bool IsDefault { get; init; }

    /// <summary>Title text (<c>{{placeholder}}</c> syntax). Empty keeps the channel's built-in subject.</summary>
    public string TitleTemplate { get; init; } = "";

    /// <summary>Body for a fired notification. Empty keeps the built-in wording.</summary>
    public string BodyTemplate { get; init; } = "";

    /// <summary>Body for a resolved notification. Empty falls back to <see cref="BodyTemplate"/>.</summary>
    public string ResolvedBodyTemplate { get; init; } = "";

    /// <summary>
    /// Fired-body override per <see cref="NotificationChannelType"/> name (e.g. a short one for
    /// Telegram, a long one for Email). A channel type with no entry uses <see cref="BodyTemplate"/>.
    /// </summary>
    public IReadOnlyDictionary<string, string> ChannelBodies { get; init; } = new Dictionary<string, string>();

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>Create/update request body for <c>/api/alert-templates</c>.</summary>
public sealed record AlertTemplateRequest
{
    public const int MaxNameLength = 128;

    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Make this the instance default; any other default is cleared. Omitted means false.</summary>
    public bool? IsDefault { get; init; }

    public string? TitleTemplate { get; init; }

    public string? BodyTemplate { get; init; }

    public string? ResolvedBodyTemplate { get; init; }

    public IReadOnlyDictionary<string, string>? ChannelBodies { get; init; }

    /// <summary>Null when valid; otherwise the first problem, phrased for a 400 body.</summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            return "name is required.";
        }

        if (Name.Trim().Length > MaxNameLength)
        {
            return $"name must be at most {MaxNameLength} characters.";
        }

        var error = Alerting.AlertTemplateRenderer.Validate(TitleTemplate, "titleTemplate", AlertRuleRequest.MaxNotificationTitleLength)
            ?? Alerting.AlertTemplateRenderer.Validate(BodyTemplate, "bodyTemplate", AlertRuleRequest.MaxNotificationBodyLength)
            ?? Alerting.AlertTemplateRenderer.Validate(ResolvedBodyTemplate, "resolvedBodyTemplate", AlertRuleRequest.MaxNotificationBodyLength);
        if (error is not null)
        {
            return error;
        }

        foreach (var (type, body) in ChannelBodies ?? new Dictionary<string, string>())
        {
            if (!Enum.TryParse<NotificationChannelType>(type, ignoreCase: false, out var parsed) || !Enum.IsDefined(parsed))
            {
                return $"channelBodies has unknown channel type '{type}'. Supported: {string.Join(", ", Enum.GetNames<NotificationChannelType>())}.";
            }

            if (Alerting.AlertTemplateRenderer.Validate(body, $"channelBodies.{type}", AlertRuleRequest.MaxNotificationBodyLength) is { } bodyError)
            {
                return bodyError;
            }
        }

        var hasText = !string.IsNullOrEmpty(TitleTemplate) || !string.IsNullOrEmpty(BodyTemplate)
            || !string.IsNullOrEmpty(ResolvedBodyTemplate) || (ChannelBodies?.Values.Any(v => !string.IsNullOrEmpty(v)) ?? false);
        return hasText ? null : "a template needs at least one of titleTemplate, bodyTemplate, resolvedBodyTemplate or channelBodies.";
    }
}

/// <summary>A rule that references a template - what a refused delete lists.</summary>
public sealed record AlertTemplateUsage(Guid RuleId, string RuleName);
