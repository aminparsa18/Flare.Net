using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>
/// The <c>/messaging</c> page's Query API: <c>POST /api/messaging/destinations</c> (one row
/// per topic/queue) and <c>POST /api/messaging/destination-detail</c> (one destination's
/// producers, consumers, partitions and consumer lag). Same POST+body convention as
/// <see cref="ExceptionEndpoints"/>. See docs-internal/adr/0056-messaging-queue-monitoring.md.
/// </summary>
public static class MessagingEndpoints
{
    public static IEndpointRouteBuilder MapMessagingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/messaging/destinations", HandleGetDestinationsAsync);
        endpoints.MapPost("/api/messaging/destination-detail", HandleGetDestinationDetailAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleGetDestinationsAsync(
        HttpContext http,
        IMessagingQueryService queryService,
        CancellationToken cancellationToken)
    {
        MessagingDestinationsRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, MessagingJsonContext.Default.MessagingDestinationsRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        request ??= new MessagingDestinationsRequest();

        var response = await queryService.GetDestinationsAsync(request, cancellationToken);
        return ApiSerialization.Write(http, response, MessagingJsonContext.Default.MessagingDestinationsResponse);
    }

    private static async Task<IResult> HandleGetDestinationDetailAsync(
        HttpContext http,
        IMessagingQueryService queryService,
        CancellationToken cancellationToken)
    {
        MessagingDestinationDetailRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, MessagingJsonContext.Default.MessagingDestinationDetailRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null || string.IsNullOrEmpty(request.System))
        {
            return Results.Problem("A request body with a system is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var response = await queryService.GetDestinationDetailAsync(request, cancellationToken);
        return ApiSerialization.Write(http, response, MessagingJsonContext.Default.MessagingDestinationDetailResponse);
    }
}
