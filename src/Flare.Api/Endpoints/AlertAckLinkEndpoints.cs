using System.Text.Json;
using Flare.Api.Alerting;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>
/// Acknowledge a firing alert from the link in its notification (<c>{{ack_url}}</c>, ADR-0127).
/// Mapped outside the authenticated groups, like <c>/api/auth/set-password</c>: the signed token
/// is the credential, so a responder who is not signed in to Flare can still take ownership.
/// </summary>
/// <remarks>
/// Two routes so a link never acts on a bare GET (mail scanners and chat unfurlers fetch every
/// URL they see): GET describes what the token acknowledges for the confirmation page, POST
/// records the ack. Both fail the same way for a bad, expired or stale link so the response
/// reveals nothing about which rules exist.
/// </remarks>
public static class AlertAckLinkEndpoints
{
    /// <summary><see cref="AlertAck.AckedBy"/> for a link redeemed with no session.</summary>
    internal const string AnonymousAckedBy = "notification link";

    public static IEndpointRouteBuilder MapAlertAckLinkEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/alerts/ack-link", (string? token, IAlertAckLinkSigner signer, IAlertQueryService alerts, CancellationToken ct) => HandleInfoAsync(token, signer, alerts, ct));
        endpoints.MapPost("/api/alerts/ack-link", HandleRedeemAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleInfoAsync(string? token, IAlertAckLinkSigner signer, IAlertQueryService alerts, CancellationToken cancellationToken)
    {
        var resolved = await ResolveAsync(token, signer, alerts, cancellationToken);
        return resolved.Failure ?? Results.Json(new AlertAckLinkInfo(resolved.Rule!.Name, resolved.State!.Ack), AlertsJsonContext.Default.AlertAckLinkInfo);
    }

    private static async Task<IResult> HandleRedeemAsync(HttpContext http, IAlertAckLinkSigner signer, IAlertQueryService alerts, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        AlertAckLinkRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, AlertsJsonContext.Default.AlertAckLinkRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        var resolved = await ResolveAsync(request?.Token, signer, alerts, cancellationToken);
        if (resolved.Failure is not null)
        {
            return resolved.Failure;
        }

        var ack = new AlertAck(resolved.Rule!.Id, timeProvider.GetUtcNow(), http.User.Identity?.Name ?? AnonymousAckedBy, AlertAckKind.Ack, null, "");
        await alerts.InsertAckAsync(ack, cancellationToken);
        return Results.Json(new AlertAckLinkInfo(resolved.Rule.Name, AlertAckPolicy.Effective(ack, resolved.State!.LastResolvedAt)), AlertsJsonContext.Default.AlertAckLinkInfo);
    }

    /// <summary>The rule and its live incident behind <paramref name="token"/>, or the failure response (see <see cref="AlertAckResolver"/>).</summary>
    private static async Task<(AlertRule? Rule, AlertFiringState? State, IResult? Failure)> ResolveAsync(string? token, IAlertAckLinkSigner signer, IAlertQueryService alerts, CancellationToken cancellationToken)
    {
        var resolution = await AlertAckResolver.ResolveTokenAsync(token, signer, alerts, cancellationToken);
        return resolution.Failure switch
        {
            AlertAckFailure.InvalidToken => (null, null, Results.Problem("This link is invalid or has expired.", statusCode: StatusCodes.Status400BadRequest)),
            AlertAckFailure.NotFiring => (null, null, Results.Problem("This alert is no longer firing, so there is nothing to acknowledge.", statusCode: StatusCodes.Status409Conflict)),
            _ => (resolution.Rule, resolution.State, null),
        };
    }
}
