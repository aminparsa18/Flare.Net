using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>
/// Continuous-profiling Query API: <c>POST /api/profiles/types</c> (which service/sample-type
/// series exist) and <c>POST /api/profiles/flamegraph</c> (the merged call tree, optionally for
/// one span). Same POST+body convention as <see cref="ExternalApiEndpoints"/>. See
/// docs-internal/adr/0141-continuous-profiling-ingest.md.
/// </summary>
public static class ProfileEndpoints
{
    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/profiles/types", HandleGetTypesAsync);
        endpoints.MapPost("/api/profiles/flamegraph", HandleGetFlameGraphAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleGetTypesAsync(
        HttpContext http,
        IProfileQueryService queryService,
        CancellationToken cancellationToken)
    {
        ProfileTypesRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, ProfileJsonContext.Default.ProfileTypesRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        var response = await queryService.GetTypesAsync(request ?? new ProfileTypesRequest(), cancellationToken);
        return ApiSerialization.Write(http, response, ProfileJsonContext.Default.ProfileTypesResponse);
    }

    private static async Task<IResult> HandleGetFlameGraphAsync(
        HttpContext http,
        IProfileQueryService queryService,
        CancellationToken cancellationToken)
    {
        FlameGraphRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, ProfileJsonContext.Default.FlameGraphRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Service) || string.IsNullOrWhiteSpace(request.SampleType))
        {
            return Results.Problem("A request body with a service and a sampleType is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var response = await queryService.GetFlameGraphAsync(request, cancellationToken);
        return ApiSerialization.Write(http, response, ProfileJsonContext.Default.FlameGraphResponse);
    }
}
