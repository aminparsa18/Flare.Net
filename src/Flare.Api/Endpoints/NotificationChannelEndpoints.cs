using System.Text.Json;
using Flare.Api.Alerting;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>
/// The notification-channel API: channel CRUD and a "send test" dry-run under
/// <c>/api/notification-channels</c> - mirrors <see cref="AlertEndpoints"/>'s shape
/// (manual <see cref="JsonSerializer"/> against a source-gen JSON context,
/// <see cref="Results.Problem"/> on 400s), for the reusable-channel entity a rule
/// references by ID via <see cref="AlertRule.ChannelIds"/> instead of embedding a
/// destination inline. See <c>docs-internal/adr/0021-reusable-notification-channels.md</c>.
/// </summary>
public static class NotificationChannelEndpoints
{
    public static IEndpointRouteBuilder MapNotificationChannelEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/notification-channels", HandleCreateAsync);
        endpoints.MapGet("/api/notification-channels", HandleListAsync);
        endpoints.MapGet("/api/notification-channels/{id:guid}", HandleGetAsync);
        endpoints.MapPut("/api/notification-channels/{id:guid}", HandleUpdateAsync);
        endpoints.MapDelete("/api/notification-channels/{id:guid}", HandleDeleteAsync);
        endpoints.MapPost("/api/notification-channels/{id:guid}/send-test", HandleSendTestAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleCreateAsync(HttpContext http, INotificationChannelQueryService channels, CancellationToken cancellationToken)
    {
        NotificationChannelRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, NotificationChannelsJsonContext.Default.NotificationChannelRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null)
        {
            return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.ValidateDestination() is { } error)
        {
            return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
        }

        var channel = await channels.CreateAsync(request, cancellationToken);
        return ApiSerialization.Write(http, channel, NotificationChannelsJsonContext.Default.NotificationChannel, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> HandleListAsync(HttpContext http, INotificationChannelQueryService channels, CancellationToken cancellationToken)
    {
        var list = await channels.ListAsync(cancellationToken);
        return ApiSerialization.Write(http, new NotificationChannelListResponse { Channels = list }, NotificationChannelsJsonContext.Default.NotificationChannelListResponse);
    }

    private static async Task<IResult> HandleGetAsync(Guid id, HttpContext http, INotificationChannelQueryService channels, CancellationToken cancellationToken)
    {
        var channel = await channels.GetAsync(id, cancellationToken);
        return channel is null ? Results.NotFound() : ApiSerialization.Write(http, channel, NotificationChannelsJsonContext.Default.NotificationChannel);
    }

    private static async Task<IResult> HandleUpdateAsync(Guid id, HttpContext http, INotificationChannelQueryService channels, CancellationToken cancellationToken)
    {
        NotificationChannelRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, NotificationChannelsJsonContext.Default.NotificationChannelRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null)
        {
            return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.ValidateDestination() is { } error)
        {
            return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
        }

        var channel = await channels.UpdateAsync(id, request, cancellationToken);
        return channel is null ? Results.NotFound() : ApiSerialization.Write(http, channel, NotificationChannelsJsonContext.Default.NotificationChannel);
    }

    private static async Task<IResult> HandleDeleteAsync(Guid id, INotificationChannelQueryService channels, CancellationToken cancellationToken)
    {
        var deleted = await channels.DeleteAsync(id, cancellationToken);
        return deleted ? Results.NoContent() : Results.NotFound();
    }

    /// <summary>
    /// Sends a synthetic (non-breaching) test notification through this one channel,
    /// independent of any alert rule - unlike <c>AlertEndpoints</c>'s rule-scoped
    /// send-test, which resolves a rule's configured channel(s) first. Wraps the channel
    /// in a minimal ephemeral <see cref="AlertRule"/> (<see cref="Guid.Empty"/> id, same
    /// "not a real persisted row" sentinel <see cref="NotificationChannelResolver.LegacyChannelId"/>
    /// and <c>AlertEndpoints.HandleSendTestDraftAsync</c>'s own draft rule already use)
    /// purely so <see cref="AlertMessageFormatter.BuildText"/> has a name to put in the
    /// test message - this never touches <c>alert_rules</c>/<c>alert_events</c>.
    /// </summary>
    private static async Task<IResult> HandleSendTestAsync(Guid id, HttpContext http, INotificationChannelQueryService channels, CompositeAlertNotifier notifier, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var channel = await channels.GetAsync(id, cancellationToken);
        if (channel is null)
        {
            return Results.NotFound();
        }

        var now = timeProvider.GetUtcNow();
        var syntheticRule = new AlertRule
        {
            Id = Guid.Empty,
            Name = channel.Name,
            Condition = new LogFilter(),
            Threshold = new AlertThreshold { Count = 0 },
            WindowSeconds = 0,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var result = await notifier.SendAsync(syntheticRule, channel, observedValue: 0, now, cancellationToken, isTest: true);
        return ApiSerialization.Write(
            http,
            new AlertNotificationTestResult { Success = result.Success, StatusCode = result.StatusCode, Error = result.Error ?? "" },
            AlertsJsonContext.Default.AlertNotificationTestResult);
    }
}
