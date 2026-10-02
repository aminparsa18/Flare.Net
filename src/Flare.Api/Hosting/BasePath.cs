namespace Flare.Api.Hosting;

/// <summary>
/// Sub-path hosting (<c>docs/how-to/serve-under-a-sub-path.md</c>): when Flare is served at
/// <c>https://example.com/flare/</c>, <c>Flare:BasePath</c> (env <c>Flare__BasePath</c>) is
/// <c>/flare</c>. The API's own routes stay <c>/api/...</c>; the base path only matters
/// wherever the app emits a URL (OIDC/Entra redirects and displayed redirect URIs, cookie
/// scope) - ASP.NET Core derives those from <see cref="HttpRequest.PathBase"/>.
/// </summary>
public static class BasePath
{
    public const string ConfigurationKey = "Flare:BasePath";

    /// <summary>
    /// <c>null</c>/blank/<c>"/"</c> become <c>""</c> (served at the root); <c>"flare/"</c> and
    /// <c>"/flare/"</c> become <c>"/flare"</c>. Throws on a value that can't be spliced into a
    /// URL path, so a typo fails at startup instead of producing broken redirects.
    /// </summary>
    public static string Normalize(string? raw)
    {
        var trimmed = (raw ?? "").Trim().Trim('/');
        if (trimmed.Length == 0)
        {
            return "";
        }

        foreach (var segment in trimmed.Split('/'))
        {
            if (segment.Length == 0 || !segment.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '~' or '-'))
            {
                throw new InvalidOperationException(
                    $"{ConfigurationKey} \"{raw}\" is not a valid URL path prefix - use something like /flare or /tools/flare.");
            }
        }

        return $"/{trimmed}";
    }

    /// <summary>
    /// Makes <paramref name="basePath"/> the request's <see cref="HttpRequest.PathBase"/>
    /// whether or not the reverse proxy strips it: a request that arrives as
    /// <c>/flare/api/...</c> has the prefix moved from <see cref="HttpRequest.Path"/> to
    /// PathBase (what <c>UsePathBase</c> does), and one that arrives as plain <c>/api/...</c>
    /// (proxy already stripped it, or a direct health check) gets PathBase set anyway -
    /// <c>UsePathBase</c> alone would leave it empty there and every generated redirect would
    /// lose the prefix.
    /// </summary>
    public static IApplicationBuilder UseFlareBasePath(this IApplicationBuilder app, string basePath)
    {
        if (basePath.Length == 0)
        {
            return app;
        }

        var pathBase = new PathString(basePath);
        return app.Use((context, next) =>
        {
            if (context.Request.Path.StartsWithSegments(pathBase, out var remaining))
            {
                context.Request.PathBase = context.Request.PathBase.Add(pathBase);
                context.Request.Path = remaining;
            }
            else
            {
                context.Request.PathBase = context.Request.PathBase.Add(pathBase);
            }

            return next();
        });
    }
}
