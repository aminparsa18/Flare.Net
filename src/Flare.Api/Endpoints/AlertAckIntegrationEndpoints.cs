using System.Text.Json;
using Flare.Api.Alerting;
using Flare.Api.Model;
using Flare.Api.Query;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Flare.Api.Endpoints;

/// <summary>
/// Inbound ack integrations (ADR-0138): the Slack Acknowledge button and PagerDuty acknowledgement
/// sync. Mapped outside the authenticated groups; the request signature is the credential, and an
/// endpoint whose secret is not configured answers 404 as if it did not exist.
/// </summary>
public static class AlertAckIntegrationEndpoints
{
    private const int MaxBodyBytes = 64 * 1024;

    internal const string SlackAckedByPrefix = "Slack: ";
    internal const string PagerDutyAckedByPrefix = "PagerDuty: ";

    public static IEndpointRouteBuilder MapAlertAckIntegrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/alerts/slack-interactivity", HandleSlackAsync);
        endpoints.MapPost("/api/alerts/pagerduty-webhook", HandlePagerDutyAsync);
        return endpoints;
    }

    internal static async Task<IResult> HandleSlackAsync(HttpContext http, IOptions<AlertLinkOptions> options, IAlertAckLinkSigner signer, IAlertQueryService alerts, IHttpClientFactory httpClients, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var secret = options.Value.SlackSigningSecret;
        if (string.IsNullOrEmpty(secret))
        {
            return Results.NotFound();
        }

        if (await ReadBodyAsync(http, cancellationToken) is not { } raw)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var body = System.Text.Encoding.UTF8.GetString(raw);
        if (!InboundAckSignatures.IsValidSlack(secret, http.Request.Headers["X-Slack-Request-Timestamp"], http.Request.Headers["X-Slack-Signature"], body, timeProvider.GetUtcNow()))
        {
            return Results.Unauthorized();
        }

        if (!QueryHelpers.ParseQuery(body).TryGetValue("payload", out var payloadValues) || SlackAction.TryParse(payloadValues.ToString()) is not { } click)
        {
            // Another interaction type (or a button that isn't ours): acknowledge receipt, do nothing.
            return Results.Ok();
        }

        var resolution = await AlertAckResolver.ResolveTokenAsync(click.Token, signer, alerts, cancellationToken);
        string reply;
        bool inChannel = false;
        if (resolution.Failure == AlertAckFailure.InvalidToken)
        {
            reply = "This Acknowledge button has expired. Use the link in a newer notification, or acknowledge in the Flare dashboard.";
        }
        else if (resolution.Failure == AlertAckFailure.NotFiring)
        {
            reply = "This alert is no longer firing, so there is nothing to acknowledge.";
        }
        else if (resolution.State!.Ack is { Kind: AlertAckKind.Ack } existing)
        {
            reply = $"*{resolution.Rule!.Name}* is already acknowledged by {(string.IsNullOrEmpty(existing.AckedBy) ? "someone" : existing.AckedBy)}.";
        }
        else
        {
            await alerts.InsertAckAsync(new AlertAck(resolution.Rule!.Id, timeProvider.GetUtcNow(), SlackAckedByPrefix + click.User, AlertAckKind.Ack, null, ""), cancellationToken);
            reply = $":white_check_mark: *{resolution.Rule.Name}* acknowledged by {click.User}.";
            inChannel = true;
        }

        await RespondAsync(httpClients, click.ResponseUrl, reply, inChannel, cancellationToken);
        return Results.Ok();
    }

    internal static async Task<IResult> HandlePagerDutyAsync(HttpContext http, IOptions<AlertLinkOptions> options, IAlertQueryService alerts, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var secret = options.Value.PagerDutyWebhookSecret;
        if (string.IsNullOrEmpty(secret))
        {
            return Results.NotFound();
        }

        if (await ReadBodyAsync(http, cancellationToken) is not { } raw)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        if (!InboundAckSignatures.IsValidPagerDuty(secret, http.Request.Headers["X-PagerDuty-Signature"], raw))
        {
            return Results.Unauthorized();
        }

        // From here the request is authentic: answer 200 whatever it holds, so PagerDuty never retries
        // (or disables the subscription over) an event Flare has no use for.
        if (PagerDutyEvent.TryParse(raw) is not { } evt)
        {
            return Results.Ok();
        }

        var resolution = await AlertAckResolver.ResolveRuleAsync(evt.RuleId, null, alerts, cancellationToken);
        if (resolution.Failure != AlertAckFailure.None)
        {
            return Results.Ok();
        }

        var current = resolution.State!.Ack;
        if (evt.Acknowledged && current is not { Kind: AlertAckKind.Ack })
        {
            await alerts.InsertAckAsync(new AlertAck(resolution.Rule!.Id, timeProvider.GetUtcNow(), PagerDutyAckedByPrefix + evt.Agent, AlertAckKind.Ack, null, ""), cancellationToken);
        }
        else if (!evt.Acknowledged && current is { Kind: AlertAckKind.Ack } && current.AckedBy.StartsWith(PagerDutyAckedByPrefix, StringComparison.Ordinal))
        {
            // PagerDuty's ack timed out or was withdrawn. Only undo an ack that came from PagerDuty:
            // one made in Flare is Flare's to clear.
            await alerts.InsertAckAsync(new AlertAck(resolution.Rule!.Id, timeProvider.GetUtcNow(), PagerDutyAckedByPrefix + evt.Agent, AlertAckKind.Clear, null, ""), cancellationToken);
        }

        return Results.Ok();
    }

    private static async Task<byte[]?> ReadBodyAsync(HttpContext http, CancellationToken cancellationToken)
    {
        if (http.Request.ContentLength > MaxBodyBytes)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await http.Request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>Posts <paramref name="text"/> to the interaction's <c>response_url</c>, which only ever points at Slack; a failure is not worth failing the click over.</summary>
    private static async Task RespondAsync(IHttpClientFactory httpClients, string? responseUrl, string text, bool inChannel, CancellationToken cancellationToken)
    {
        if (!WebhookAlertNotifier.IsSlackWebhook(responseUrl) || !responseUrl!.StartsWith("https://", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            using var response = await httpClients.CreateClient().PostAsJsonAsync(responseUrl, new { response_type = inChannel ? "in_channel" : "ephemeral", replace_original = false, text }, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
        }
    }
}

/// <summary>The Acknowledge button click inside a Slack <c>block_actions</c> payload.</summary>
internal sealed record SlackAction(string Token, string User, string? ResponseUrl)
{
    /// <summary>The click, or null for any other payload (another interaction type, another button, malformed JSON).</summary>
    public static SlackAction? TryParse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Str(root, "type") != "block_actions" || !root.TryGetProperty("actions", out var actions) || actions.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var action in actions.EnumerateArray())
            {
                if (Str(action, "action_id") == WebhookAlertNotifier.SlackAckActionId && Str(action, "value") is { Length: > 0 } token)
                {
                    var user = root.TryGetProperty("user", out var u) ? Str(u, "name") ?? Str(u, "username") ?? Str(u, "id") : null;
                    return new SlackAction(token, user ?? "unknown", Str(root, "response_url"));
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

/// <summary>An incident acknowledged or unacknowledged, from a PagerDuty V3 webhook payload.</summary>
internal sealed record PagerDutyEvent(Guid RuleId, bool Acknowledged, string Agent)
{
    /// <summary>The event, or null when it is another type, belongs to an incident Flare did not open, or is malformed.</summary>
    public static PagerDutyEvent? TryParse(ReadOnlySpan<byte> json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json.ToArray());
            if (!doc.RootElement.TryGetProperty("event", out var evt) || evt.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var acknowledged = Str(evt, "event_type") switch
            {
                "incident.acknowledged" => true,
                "incident.unacknowledged" => false,
                _ => (bool?)null,
            };
            if (acknowledged is null || !evt.TryGetProperty("data", out var data) || PagerDutyAlertNotifier.RuleIdFromDedupKey(Str(data, "incident_key")) is not { } ruleId)
            {
                return null;
            }

            var agent = evt.TryGetProperty("agent", out var a) ? Str(a, "summary") ?? Str(a, "id") : null;
            return new PagerDutyEvent(ruleId, acknowledged.Value, agent ?? "unknown");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
