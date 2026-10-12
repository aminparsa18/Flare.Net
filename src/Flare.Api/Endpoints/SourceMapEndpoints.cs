using System.IO.Compression;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.SourceMaps;
using Flare.Identity.SourceMaps;
using Flare.Mcp.DotnetSymbols;
using Microsoft.AspNetCore.Http.Features;

namespace Flare.Api.Endpoints;

/// <summary>
/// Source-map upload for symbolicating browser stack traces on /errors - see
/// docs-internal/adr/0152-source-map-upload.md. Listing is open to any signed-in user;
/// <see cref="MapSourceMapWriteEndpoints"/> (upload, delete) is Admin-only and is what a CI job
/// calls with a personal access token, so both are mapped onto different route groups in
/// <c>Program.cs</c>.
/// </summary>
public static class SourceMapEndpoints
{
    /// <summary>Largest accepted map, uncompressed. Real production maps are tens of MB at most.</summary>
    public const int MaxMapBytes = 50 * 1024 * 1024;

    public static IEndpointRouteBuilder MapSourceMapReadEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/source-maps", HandleListAsync);
        return endpoints;
    }

    public static IEndpointRouteBuilder MapSourceMapWriteEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/api/source-maps", HandleUploadAsync);
        endpoints.MapDelete("/api/source-maps", HandleDeleteAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleListAsync(HttpContext http, ISourceMapStore store, CancellationToken cancellationToken)
    {
        var maps = await store.ListAsync(Param(http, "service"), Param(http, "version"), cancellationToken);
        var response = new SourceMapListResponse
        {
            Maps = [.. maps.Select(m => new SourceMapDto { ServiceName = m.ServiceName, Version = m.Version, Bundle = m.Bundle, SizeBytes = m.SizeBytes, UploadedAt = m.UploadedAt })]
        };
        return ApiSerialization.Write(http, response, SourceMapsJsonContext.Default.SourceMapListResponse);
    }

    /// <summary>
    /// <c>PUT /api/source-maps?service=&amp;version=&amp;bundle=</c> with the raw <c>.map</c> JSON as the body.
    /// <c>bundle</c> is the served script's path (e.g. <c>assets/index-abc.js</c>); a frame's URL matches
    /// when its path ends with it.
    /// </summary>
    private static async Task<IResult> HandleUploadAsync(HttpContext http, ISourceMapStore store, CancellationToken cancellationToken)
    {
        if (Required(http) is not var (service, version, bundle))
        {
            return Results.Problem("service, version and bundle query parameters are required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
        {
            limit.MaxRequestBodySize = MaxMapBytes + 1;
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await http.Request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxMapBytes)
            {
                return Results.Problem($"A source map may be at most {MaxMapBytes / (1024 * 1024)} MB.", statusCode: StatusCodes.Status413PayloadTooLarge);
            }

            buffer.Write(chunk, 0, read);
        }

        try
        {
            var bytes = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);
            if (bundle.EndsWith(DotnetSymbols.BundleSuffix, StringComparison.Ordinal))
            {
                // .NET symbols (ADR-0168): the bundle name must be the file's own MVID, since frames find it by that.
                if (DotnetSymbols.Parse(bytes).Mvid.ToString("N") + DotnetSymbols.BundleSuffix != bundle)
                {
                    return Results.Problem($"A .NET symbols bundle must be named {{mvid}}{DotnetSymbols.BundleSuffix} after the assembly's MVID.", statusCode: StatusCodes.Status400BadRequest);
                }
            }
            else if (bundle.EndsWith(NativeSymbols.BundleSuffix, StringComparison.Ordinal))
            {
                if (NativeSymbols.BundleName(NativeSymbols.Parse(bytes).Uuid) != bundle)
                {
                    return Results.Problem($"A native symbols bundle must be named {{uuid}}{NativeSymbols.BundleSuffix} after the image's UUID.", statusCode: StatusCodes.Status400BadRequest);
                }
            }
            else
            {
                SourceMap.Parse(bytes);
            }
        }
        catch (FormatException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        using var compressed = new MemoryStream();
        await using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            await gzip.WriteAsync(buffer.GetBuffer().AsMemory(0, (int)buffer.Length), cancellationToken);
        }

        await store.UpsertAsync(service, version, bundle, compressed.ToArray(), buffer.Length, cancellationToken);
        return Results.NoContent();
    }

    /// <summary><c>DELETE /api/source-maps?service=&amp;version=[&amp;bundle=]</c> - one bundle's map, or the whole version's.</summary>
    private static async Task<IResult> HandleDeleteAsync(HttpContext http, ISourceMapStore store, CancellationToken cancellationToken)
    {
        var service = Param(http, "service");
        var version = Param(http, "version");
        if (service is null || version is null)
        {
            return Results.Problem("service and version query parameters are required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var deleted = await store.DeleteAsync(service, version, NormalizeBundle(Param(http, "bundle")), cancellationToken);
        return deleted == 0 ? Results.NotFound() : Results.NoContent();
    }

    private static (string Service, string Version, string Bundle)? Required(HttpContext http)
    {
        var service = Param(http, "service");
        var version = Param(http, "version");
        var bundle = NormalizeBundle(Param(http, "bundle"));
        return service is null || version is null || bundle is null ? null : (service, version, bundle);
    }

    private static string? Param(HttpContext http, string name) =>
        http.Request.Query[name].ToString() is { Length: > 0 } value ? value.Trim() : null;

    /// <summary>Drops a leading slash and any query or fragment, so <c>/assets/app.js?v=1</c> and <c>assets/app.js</c> are one bundle.</summary>
    internal static string? NormalizeBundle(string? bundle)
    {
        if (bundle is null)
        {
            return null;
        }

        var end = bundle.IndexOfAny(['?', '#']);
        var path = (end >= 0 ? bundle[..end] : bundle).Trim('/');
        return path.Length == 0 ? null : path;
    }
}
