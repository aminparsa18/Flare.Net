using System.Security.Claims;
using Flare.Identity.Audit;
using Flare.Identity.Auth;
using Microsoft.Extensions.Options;

namespace Flare.Api.Auditing;

/// <summary>
/// Records an <see cref="AuditEvent"/> after every successful, allowlisted state-changing
/// request (see <see cref="AuditActionClassifier"/>). Runs after authentication/authorization,
/// so a rejected request is never recorded; a failed write of the audit row is logged and
/// swallowed rather than turning a change that already happened into a 500.
/// </summary>
public sealed class AuditMiddleware(RequestDelegate next, ILogger<AuditMiddleware> logger)
{
    public async Task InvokeAsync(
        HttpContext context,
        IAuditEventStore store,
        TimeProvider timeProvider,
        IOptions<AuditOptions> options,
        AuditReadThrottle readThrottle)
    {
        await next(context);

        var status = context.Response.StatusCode;
        var template = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
        var classification = status is >= 200 and < 300
            ? AuditActionClassifier.Classify(context.Request.Method, template)
            : null;

        var user = context.User;
        Guid? actorId = Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

        // Telemetry reads (ADR-0204), opt-in. 101 is a successful live-tail WebSocket upgrade.
        if (classification is null
            && options.Value.RecordReads
            && status is (>= 200 and < 300) or 101)
        {
            classification = AuditActionClassifier.ClassifyRead(context.Request.Method, template);
            if (classification is not null
                && !readThrottle.ShouldRecord(
                    $"{actorId}|{user.Identity?.Name}|{classification.ResourceType}|{classification.Action}|{ReadResourceId(context, classification)}",
                    TimeSpan.FromSeconds(options.Value.ReadDedupeSeconds)))
            {
                return;
            }
        }

        if (classification is null)
        {
            return;
        }

        var resourceId = AuditContext.GetResourceId(context) ?? ReadResourceId(context, classification);

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
                    status,
                    context.Connection.RemoteIpAddress?.ToString(),
                    AuditContext.GetChanges(context)),
                context.RequestAborted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to record audit event for {Method} {Route}", context.Request.Method, template);
        }
    }

    private static string? ReadResourceId(HttpContext context, AuditClassification classification) =>
        classification.ResourceIdRouteValue is { } key ? context.Request.RouteValues[key]?.ToString() : null;
}
