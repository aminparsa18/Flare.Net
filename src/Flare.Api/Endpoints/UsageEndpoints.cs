using Flare.Api.Json;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>
/// The Usage page's one endpoint: <c>GET /api/usage?days=7</c>. Admin-only (mapped onto
/// Program.cs's admin group) because it lists ingest keys, same as the Ingest Keys page.
/// </summary>
public static class UsageEndpoints
{
    public static IEndpointRouteBuilder MapUsageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/usage", HandleGetAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleGetAsync(int? days, HttpContext http, IUsageQueryService service, CancellationToken cancellationToken) =>
        ApiSerialization.Write(http, await service.GetAsync(days ?? UsageShaper.DefaultDays, cancellationToken), UsageJsonContext.Default.UsageResponse);
}
