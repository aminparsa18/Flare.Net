using System.Security.Claims;
using Flare.Identity.Audit;
using Flare.Identity.Auth;

namespace Flare.Api.Auditing;

/// <summary>
/// Records an <see cref="AuditEvent"/> after every successful, allowlisted state-changing
/// request (see <see cref="AuditActionClassifier"/>). Runs after authentication/authorization,
/// so a rejected request is never recorded; a failed write of the audit row is logged and
/// swallowed rather than turning a change that already happened into a 500.
/// </summary>
public sealed class AuditMiddleware(RequestDelegate next, ILogger<AuditMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, IAuditEventStore store, TimeProvider timeProvider)
    {
        await next(context);

        if (context.Response.StatusCode is < 200 or >= 300)
        {
            return;
        }

        var template = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
        var classification = AuditActionClassifier.Classify(context.Request.Method, template);
        if (classification is null)
        {
            return;
        }

        var user = context.User;
        Guid? actorId = Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
        var resourceId = AuditContext.GetResourceId(context)
            ?? (classification.ResourceIdRouteValue is { } key ? context.Request.RouteValues[key]?.ToString() : null);

        try
        {
            await store.AppendAsync(
                new NewAuditEvent(
                    timeProvider.GetUtcNow(),
                    actorId,
                    user.Identity?.Name ?? "unknown",
                    user.HasClaim(c => c.Type == FlareClaimTypes.PersonalAccessTokenId) ? "pat" : "session",
                    classification.Action,
                    classification.ResourceType,
                    resourceId,
                    $"{context.Request.Method.ToUpperInvariant()} {template}",
                    context.Response.StatusCode,
                    context.Connection.RemoteIpAddress?.ToString()),
                context.RequestAborted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to record audit event for {Method} {Route}", context.Request.Method, template);
        }
    }
}
