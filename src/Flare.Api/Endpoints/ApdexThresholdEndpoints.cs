using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Identity.Apdex;

namespace Flare.Api.Endpoints;

/// <summary>
/// Mutating half of the Traces > Services tab's per-service Apdex threshold setting - see
/// docs-internal/adr/0032-apdex-score-per-service.md. The read side
/// (<c>GET /api/services/apdex-thresholds</c>) lives on
/// <see cref="ServicesEndpoints"/> instead, mapped onto <c>authenticatedRoutes</c> - any
/// Viewer needs it to render the tab; only these two are Admin-only
/// (<c>Program.cs</c>'s <c>adminRoutes</c> group), same "mutating a global, cross-user
/// setting is Admin-only" reasoning as <c>AuthSettingsEndpoints</c>.
/// </summary>
public static class ApdexThresholdEndpoints
{
    /// <summary>Generous upper bound purely to reject fat-fingered input (e.g. a value
    /// meant as seconds) - not a meaningful Apdex threshold in practice.</summary>
    private const int MaxThresholdMs = 600_000;

    public static IEndpointRouteBuilder MapApdexThresholdEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/api/services/apdex-thresholds/{serviceName}", HandleSetAsync);
        endpoints.MapDelete("/api/services/apdex-thresholds/{serviceName}", HandleResetAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleSetAsync(
        HttpContext http,
        string serviceName,
        IApdexThresholdStore store,
        CancellationToken cancellationToken)
    {
        SetApdexThresholdRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, ServicesJsonContext.Default.SetApdexThresholdRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null || request.ThresholdMs is < 1 or > MaxThresholdMs)
        {
            return Results.Problem($"thresholdMs must be between 1 and {MaxThresholdMs}.", statusCode: StatusCodes.Status400BadRequest);
        }

        await store.SetAsync(serviceName, request.ThresholdMs, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> HandleResetAsync(
        string serviceName,
        IApdexThresholdStore store,
        CancellationToken cancellationToken)
    {
        await store.ResetAsync(serviceName, cancellationToken);
        return Results.NoContent();
    }
}
