using Flare.Api.Json;
using Flare.Api.Updates;

namespace Flare.Api.Endpoints;

/// <summary>
/// <c>GET /api/version</c> - the running version and, when a newer Flare release exists on
/// GitHub, that release, for the dashboard's "new version available" notice (ADR-0068).
/// Behind authentication like every other read endpoint; the version itself isn't secret,
/// but there's no reason to hand it to an anonymous caller either.
/// </summary>
public static class VersionEndpoints
{
    public static IEndpointRouteBuilder MapVersionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/version", HandleGetAsync);
        return endpoints;
    }

    // Results.Json, not ApiSerialization.Write - the response isn't [MemoryPackable], so an
    // Accept: application/x-memorypack caller gets JSON rather than a serializer exception.
    private static async Task<IResult> HandleGetAsync(HttpContext http, ReleaseCheckService releaseCheck) =>
        Results.Json(await releaseCheck.GetAsync(http.RequestAborted), VersionJsonContext.Default.VersionInfoResponse);
}
