using Microsoft.Extensions.Primitives;

namespace Flare.Api.Hosting;

/// <summary>
/// <c>Security:Headers:*</c> (ADR-0200): baseline response headers for the JSON API. The API never serves
/// a document a browser should render, so the policy is the strictest possible one - nothing may be
/// loaded from an API response and nothing may frame it. The dashboard sets its own (looser) CSP.
/// </summary>
public sealed class SecurityHeadersOptions
{
    public const string SectionName = "Security:Headers";

    /// <summary>Master switch. Set <c>false</c> when a reverse proxy already owns these headers.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary><c>Strict-Transport-Security</c> max-age, only sent on HTTPS requests. <c>0</c> disables HSTS.</summary>
    public int HstsMaxAgeSeconds { get; set; } = 15552000; // 180 days

    /// <summary>Adds <c>includeSubDomains</c> to HSTS. Off by default: it would pin every sibling host too.</summary>
    public bool HstsIncludeSubDomains { get; set; }

    /// <summary>CSP <c>frame-ancestors</c> source list. Default <c>'none'</c>; e.g. <c>'self' https://wiki.example.com</c>.</summary>
    public string FrameAncestors { get; set; } = "'none'";
}

public static class SecurityHeaders
{
    /// <summary>
    /// Headers for one response. Pure so it can be unit-tested; <paramref name="isHttps"/> is whether the
    /// client's connection was HTTPS (directly or via <c>X-Forwarded-Proto</c>).
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Build(SecurityHeadersOptions options, bool isHttps)
    {
        var frameAncestors = string.IsNullOrWhiteSpace(options.FrameAncestors) ? "'none'" : options.FrameAncestors.Trim();
        var headers = new List<KeyValuePair<string, string>>
        {
            new("X-Content-Type-Options", "nosniff"),
            new("Referrer-Policy", "no-referrer"),
            new("Content-Security-Policy", $"default-src 'none'; frame-ancestors {frameAncestors}"),
        };

        if (isHttps && options.HstsMaxAgeSeconds > 0)
        {
            headers.Add(new("Strict-Transport-Security",
                $"max-age={options.HstsMaxAgeSeconds}" + (options.HstsIncludeSubDomains ? "; includeSubDomains" : "")));
        }

        return headers;
    }

    /// <summary>
    /// Adds the headers to every response without overwriting one an endpoint already chose. The
    /// <c>X-Forwarded-Proto</c> check is deliberately not gated on trusted proxies: a spoofed value only
    /// changes whether the spoofer's own response carries HSTS, and browsers ignore HSTS over plain HTTP.
    /// Skipped in Development so the OpenAPI document stays viewable.
    /// </summary>
    public static IApplicationBuilder UseFlareSecurityHeaders(this IApplicationBuilder app, SecurityHeadersOptions options, bool isDevelopment)
    {
        if (!options.Enabled)
        {
            return app;
        }

        return app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var forwarded = context.Request.Headers["X-Forwarded-Proto"].ToString();
                var isHttps = context.Request.IsHttps || forwarded.Split(',')[0].Trim().Equals("https", StringComparison.OrdinalIgnoreCase);
                foreach (var (name, value) in Build(options, isHttps))
                {
                    if (isDevelopment && name == "Content-Security-Policy")
                    {
                        continue;
                    }

                    if (!context.Response.Headers.ContainsKey(name))
                    {
                        context.Response.Headers[name] = new StringValues(value);
                    }
                }

                return Task.CompletedTask;
            });
            return next();
        });
    }
}
