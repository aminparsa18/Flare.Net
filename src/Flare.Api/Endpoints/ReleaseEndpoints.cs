using System.Text.Json;
using Flare.Api.Auditing;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>
/// Release markers: <c>GET /api/releases</c> and <c>GET /api/releases/errors</c> (any authenticated
/// user), <c>PUT</c> and <c>DELETE /api/releases</c> (Member/Admin - a deploy pipeline calls these
/// with a personal access token). See <c>docs-internal/adr/0182-release-tracking.md</c>.
/// </summary>
public static class ReleaseEndpoints
{
    public static IEndpointRouteBuilder MapReleaseReadEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/releases", HandleListAsync);
        endpoints.MapGet("/api/releases/errors", HandleErrorsAsync);
        return endpoints;
    }

    public static IEndpointRouteBuilder MapReleaseWriteEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/api/releases", HandleUpsertAsync);
        endpoints.MapDelete("/api/releases", HandleDeleteAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleListAsync(HttpContext http, IReleaseQueryService releases, IErrorIssueQueryService issues, string? service, CancellationToken cancellationToken)
    {
        var list = await releases.ListAsync(service, cancellationToken);
        if (!string.IsNullOrEmpty(service) && list.Count > 0)
        {
            var byVersion = (await RegressedAsync(issues, cancellationToken)).ToLookup(i => i.RegressedVersion);
            list = list.Select(r => r with { RegressionCount = byVersion[r.Version].LongCount() }).ToList();
        }

        var response = new ReleaseListResponse { Releases = list };
        return ApiSerialization.Write(http, response, ReleasesJsonContext.Default.ReleaseListResponse);
    }

    /// <summary>Issues whose effective status is Regressed (recurrence in a version they weren't known in, ADR-0121).</summary>
    private static async Task<IReadOnlyList<ErrorIssue>> RegressedAsync(IErrorIssueQueryService issues, CancellationToken cancellationToken) =>
        (await issues.ListAsync(cancellationToken)).Where(i => i.Status == ErrorIssueStatus.Regressed).ToList();

    private static async Task<IResult> HandleErrorsAsync(HttpContext http, IReleaseQueryService releases, IErrorIssueQueryService issues, string? service, string? version, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(service) || string.IsNullOrWhiteSpace(version))
        {
            return Results.Problem("service and version are required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var errors = await releases.ListNewErrorsAsync(service, version, cancellationToken);
        if (errors is null)
        {
            return Results.Problem("No such release.", statusCode: StatusCodes.Status404NotFound);
        }

        var regressions = (await RegressedAsync(issues, cancellationToken))
            .Where(i => i.RegressedVersion == version)
            .Select(i => new ReleaseRegression
            {
                ExceptionType = i.ExceptionType,
                ExceptionMessage = i.ExceptionMessage,
                ResolvedAtUnixMs = i.StatusChangedAt.ToUnixTimeMilliseconds(),
                Assignee = i.Assignee,
            })
            .ToList();
        var response = new ReleaseErrorsResponse { Errors = errors, HistoryDays = ReleaseQueryBuilder.HistoryDays, Regressions = regressions };
        return ApiSerialization.Write(http, response, ReleasesJsonContext.Default.ReleaseErrorsResponse);
    }

    private static async Task<IResult> HandleUpsertAsync(HttpContext http, IReleaseQueryService releases, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        ReleaseRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, ReleasesJsonContext.Default.ReleaseRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null)
        {
            return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.Validate(timeProvider.GetUtcNow()) is { } error)
        {
            return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
        }

        if (!ServiceScope.Allows(request.Service))
        {
            return Results.Forbid();
        }

        var release = await releases.UpsertAsync(request, http.User.Identity?.Name ?? "", cancellationToken);
        AuditContext.SetResourceId(http, release.Id);
        return ApiSerialization.Write(http, release, ReleasesJsonContext.Default.Release);
    }

    private static async Task<IResult> HandleDeleteAsync(HttpContext http, IReleaseQueryService releases, string? service, string? version, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(service) || string.IsNullOrWhiteSpace(version))
        {
            return Results.Problem("service and version are required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (!ServiceScope.Allows(service))
        {
            return Results.Forbid();
        }

        AuditContext.SetResourceId(http, ReleaseQueryBuilder.ComputeId(service, version));
        return await releases.DeleteAsync(service, version, cancellationToken) ? Results.NoContent() : Results.NotFound();
    }
}
