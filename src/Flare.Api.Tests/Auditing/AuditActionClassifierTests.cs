using Flare.Api.Auditing;
using Xunit;

namespace Flare.Api.Tests.Auditing;

public class AuditActionClassifierTests
{
    [Theory]
    [InlineData("POST", "/api/alerts", "alert", "create", null)]
    [InlineData("PUT", "/api/alerts/{id:guid}", "alert", "update", "id")]
    [InlineData("DELETE", "/api/dashboards/{id:guid}", "dashboard", "delete", "id")]
    [InlineData("PATCH", "/api/users/{id:guid}/role", "user", "set-role", "id")]
    [InlineData("DELETE", "/api/access-tokens/{id:guid}", "access-token", "revoke", "id")]
    [InlineData("PUT", "/api/settings/oidc", "oidc-settings", "update", null)]
    [InlineData("DELETE", "/api/services/apdex-thresholds/{serviceName}", "apdex-threshold", "delete", "serviceName")]
    [InlineData("put", "/API/ALERTS/{id:guid}", "alert", "update", "id")]
    public void Classify_StateChangingRoute_ReturnsClassification(string method, string template, string type, string action, string? idKey)
    {
        var result = AuditActionClassifier.Classify(method, template);

        Assert.NotNull(result);
        Assert.Equal(type, result.ResourceType);
        Assert.Equal(action, result.Action);
        Assert.Equal(idKey, result.ResourceIdRouteValue);
    }

    [Theory]
    [InlineData("POST", "/api/logs/search")]
    [InlineData("POST", "/api/alerts/test")]
    [InlineData("POST", "/api/alerts/{id:guid}/send-test")]
    [InlineData("POST", "/api/pipeline-rules/preview")]
    [InlineData("POST", "/api/notification-channels/{id:guid}/send-test")]
    [InlineData("GET", "/api/alerts")]
    [InlineData("POST", "/api/auth/login")]
    public void Classify_ReadOnlyOrUnlistedRoute_ReturnsNull(string method, string template) =>
        Assert.Null(AuditActionClassifier.Classify(method, template));

    [Fact]
    public void Classify_WithNoEndpoint_ReturnsNull() =>
        Assert.Null(AuditActionClassifier.Classify("POST", null));
}
