namespace Flare.Api.Auditing;

/// <summary>What the audit log records about one state-changing request (ADR-0079).</summary>
public sealed record AuditClassification(string ResourceType, string Action, string? ResourceIdRouteValue);

/// <summary>
/// Decides whether a request is an auditable state change and, if so, what to call it. Pure
/// (route template in, classification out) so it is unit-testable without hosting.
/// </summary>
/// <remarks>
/// An allowlist, not "every non-GET": most of Flare's POST endpoints are read-only queries
/// (<c>/api/logs/search</c>, <c>/api/metrics/query</c>, ...) and would drown the log in
/// noise. The cost is that a new state-changing endpoint is not audited until it is added
/// to <see cref="Rules"/> - <c>AuditActionClassifierTests</c> pins the current table, and
/// ADR-0079 / CONTRIBUTING-style review is the backstop.
/// Matches the route <em>template</em> (<c>/api/alerts/{id:guid}</c>), not the concrete
/// path, so ids never leak into the matching.
/// </remarks>
public static class AuditActionClassifier
{
    private sealed record Rule(string Method, string Template, string ResourceType, string Action, string? IdRouteValue);

    private static readonly Rule[] Rules =
    [
        new("POST", "/api/alerts", "alert", "create", null),
        new("PUT", "/api/alerts/{id:guid}", "alert", "update", "id"),
        new("DELETE", "/api/alerts/{id:guid}", "alert", "delete", "id"),

        new("POST", "/api/notification-channels", "notification-channel", "create", null),
        new("PUT", "/api/notification-channels/{id:guid}", "notification-channel", "update", "id"),
        new("DELETE", "/api/notification-channels/{id:guid}", "notification-channel", "delete", "id"),

        new("POST", "/api/maintenance-windows", "maintenance-window", "create", null),
        new("PUT", "/api/maintenance-windows/{id:guid}", "maintenance-window", "update", "id"),
        new("DELETE", "/api/maintenance-windows/{id:guid}", "maintenance-window", "delete", "id"),

        new("POST", "/api/pipeline-rules", "pipeline-rule", "create", null),
        new("PUT", "/api/pipeline-rules/{id:guid}", "pipeline-rule", "update", "id"),
        new("DELETE", "/api/pipeline-rules/{id:guid}", "pipeline-rule", "delete", "id"),

        new("POST", "/api/metric-attribute-rules", "metric-attribute-rule", "create", null),
        new("PUT", "/api/metric-attribute-rules/{id:guid}", "metric-attribute-rule", "update", "id"),
        new("DELETE", "/api/metric-attribute-rules/{id:guid}", "metric-attribute-rule", "delete", "id"),

        new("POST", "/api/dashboards", "dashboard", "create", null),
        new("PUT", "/api/dashboards/{id:guid}", "dashboard", "update", "id"),
        new("DELETE", "/api/dashboards/{id:guid}", "dashboard", "delete", "id"),

        new("POST", "/api/views", "saved-view", "create", null),
        new("PUT", "/api/views/{id:guid}", "saved-view", "update", "id"),
        new("DELETE", "/api/views/{id:guid}", "saved-view", "delete", "id"),

        new("PATCH", "/api/users/{id:guid}/role", "user", "set-role", "id"),
        new("PATCH", "/api/users/{id:guid}/disabled", "user", "set-disabled", "id"),

        new("POST", "/api/service-accounts", "service-account", "create", null),
        new("POST", "/api/service-accounts/{id:guid}/access-tokens", "service-account", "create-token", "id"),

        new("POST", "/api/access-tokens", "access-token", "create", null),
        new("DELETE", "/api/access-tokens/{id:guid}", "access-token", "revoke", "id"),

        new("POST", "/api/ingest-keys", "ingest-key", "create", null),
        new("DELETE", "/api/ingest-keys/{id:guid}", "ingest-key", "revoke", "id"),
        new("PUT", "/api/ingest-keys/{id:guid}/limits", "ingest-key", "update-limits", "id"),

        new("PUT", "/api/settings/auth", "auth-settings", "update", null),
        new("PUT", "/api/settings/entra", "entra-settings", "update", null),
        new("PUT", "/api/settings/ldap", "ldap-settings", "update", null),
        new("PUT", "/api/settings/oidc", "oidc-settings", "update", null),
        new("PUT", "/api/settings/proxyauth", "proxy-auth-settings", "update", null),

        new("PUT", "/api/services/apdex-thresholds/{serviceName}", "apdex-threshold", "update", "serviceName"),
        new("DELETE", "/api/services/apdex-thresholds/{serviceName}", "apdex-threshold", "delete", "serviceName"),

        new("PUT", "/api/metrics/metadata-overrides", "metric-metadata", "update", null),
        new("DELETE", "/api/metrics/metadata-overrides", "metric-metadata", "delete", null),

        new("POST", "/api/indexing/promoted-attributes", "promoted-attribute", "create", null),
        new("DELETE", "/api/indexing/promoted-attributes/{columnName}", "promoted-attribute", "delete", "columnName"),
    ];

    /// <summary>Null when the request is not an audited state change.</summary>
    public static AuditClassification? Classify(string method, string? routeTemplate)
    {
        if (routeTemplate is null)
        {
            return null;
        }

        foreach (var rule in Rules)
        {
            if (string.Equals(rule.Method, method, StringComparison.OrdinalIgnoreCase)
                && string.Equals(rule.Template, routeTemplate, StringComparison.OrdinalIgnoreCase))
            {
                return new AuditClassification(rule.ResourceType, rule.Action, rule.IdRouteValue);
            }
        }

        return null;
    }
}
