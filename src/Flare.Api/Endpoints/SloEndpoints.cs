using System.Text.Json;
using Flare.Api.Auditing;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>
/// The SLO API: reads under <c>/api/slos</c> for any authenticated user (the dashboard's SLO
/// page), mutations Member/Admin only. See <c>docs-internal/adr/0108-slo-error-budgets.md</c>.
/// </summary>
public static class SloEndpoints
{
    public static IEndpointRouteBuilder MapSloReadEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/slos", HandleListAsync);
        endpoints.MapGet("/api/slos/{id:guid}", HandleGetAsync);
        endpoints.MapGet("/api/slos/{id:guid}/status", HandleStatusAsync);
        return endpoints;
    }

    public static IEndpointRouteBuilder MapSloWriteEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/slos", HandleCreateAsync);
        endpoints.MapPut("/api/slos/{id:guid}", HandleUpdateAsync);
        endpoints.MapDelete("/api/slos/{id:guid}", HandleDeleteAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleListAsync(HttpContext http, ISloQueryService slos, CancellationToken cancellationToken)
    {
        var list = await slos.ListAsync(cancellationToken);
        return ApiSerialization.Write(http, new SloListResponse { Slos = list }, SloJsonContext.Default.SloListResponse);
    }

    private static async Task<IResult> HandleGetAsync(Guid id, HttpContext http, ISloQueryService slos, CancellationToken cancellationToken)
    {
        var slo = await slos.GetAsync(id, cancellationToken);
        return slo is null ? Results.NotFound() : ApiSerialization.Write(http, slo, SloJsonContext.Default.Slo);
    }

    private static async Task<IResult> HandleStatusAsync(Guid id, HttpContext http, ISloQueryService slos, CancellationToken cancellationToken)
    {
        var slo = await slos.GetAsync(id, cancellationToken);
        if (slo is null)
        {
            return Results.NotFound();
        }

        var status = await slos.GetStatusAsync(slo, cancellationToken);
        return ApiSerialization.Write(http, status, SloJsonContext.Default.SloStatus);
    }

    private static async Task<IResult> HandleCreateAsync(HttpContext http, ISloQueryService slos, CancellationToken cancellationToken)
    {
        var (request, problem) = await ReadRequestAsync(http, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        var slo = await slos.CreateAsync(request!, cancellationToken);
        AuditContext.SetResourceId(http, slo.Id);
        return ApiSerialization.Write(http, slo, SloJsonContext.Default.Slo, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> HandleUpdateAsync(Guid id, HttpContext http, ISloQueryService slos, CancellationToken cancellationToken)
    {
        var (request, problem) = await ReadRequestAsync(http, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        var before = await slos.GetAsync(id, cancellationToken);
        var slo = await slos.UpdateAsync(id, request!, cancellationToken);
        if (slo is null)
        {
            return Results.NotFound();
        }

        AuditContext.SetChange(http, SloJsonContext.Default.Slo, before, slo);
        return ApiSerialization.Write(http, slo, SloJsonContext.Default.Slo);
    }

    private static async Task<IResult> HandleDeleteAsync(Guid id, ISloQueryService slos, CancellationToken cancellationToken)
    {
        var deleted = await slos.DeleteAsync(id, cancellationToken);
        return deleted ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<(SloRequest? Request, IResult? Problem)> ReadRequestAsync(HttpContext http, CancellationToken cancellationToken)
    {
        SloRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, SloJsonContext.Default.SloRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return (null, Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest));
        }

        if (request is null)
        {
            return (null, Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest));
        }

        if (request.Validate() is { } error)
        {
            return (null, Results.Problem(error, statusCode: StatusCodes.Status400BadRequest));
        }

        return (request, null);
    }
}
