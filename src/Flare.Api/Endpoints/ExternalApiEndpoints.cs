using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>
/// The <c>/external-apis</c> page's Query API: <c>POST /api/external-apis/domains</c> (one row
/// per external domain our services call) and <c>POST /api/external-apis/domain-detail</c> (one
/// domain's endpoints, status codes, callers and top errors). Same POST+body convention as
/// <see cref="MessagingEndpoints"/>. See docs-internal/adr/0071-external-api-monitoring.md.
/// </summary>
public static class ExternalApiEndpoints
{
    public static IEndpointRouteBuilder MapExternalApiEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/external-apis/domains", HandleGetDomainsAsync);
        endpoints.MapPost("/api/external-apis/domain-detail", HandleGetDomainDetailAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleGetDomainsAsync(
        HttpContext http,
        IExternalApiQueryService queryService,
        CancellationToken cancellationToken)
    {
        ExternalDomainsRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, ExternalApiJsonContext.Default.ExternalDomainsRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        request ??= new ExternalDomainsRequest();

        var response = await queryService.GetDomainsAsync(request, cancellationToken);
        return ApiSerialization.Write(http, response, ExternalApiJsonContext.Default.ExternalDomainsResponse);
    }

    private static async Task<IResult> HandleGetDomainDetailAsync(
        HttpContext http,
        IExternalApiQueryService queryService,
        CancellationToken cancellationToken)
    {
        ExternalDomainDetailRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, ExternalApiJsonContext.Default.ExternalDomainDetailRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null || string.IsNullOrEmpty(request.Domain))
        {
            return Results.Problem("A request body with a domain is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var response = await queryService.GetDomainDetailAsync(request, cancellationToken);
        return ApiSerialization.Write(http, response, ExternalApiJsonContext.Default.ExternalDomainDetailResponse);
    }
}
