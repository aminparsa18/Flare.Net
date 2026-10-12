using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;

namespace Flare.Api.Status;

/// <summary>
/// CORS for the public status endpoints (ADR-0165). A status page on a custom domain is served by the dashboard on
/// that domain, so its browser calls this API from an origin nobody listed in <c>Cors:AllowedOrigins</c>. Those
/// endpoints therefore accept the configured origins plus the https origin of any enabled page's domain. They are
/// unauthenticated, so credentials are never allowed; every other endpoint keeps the default policy.
/// </summary>
public sealed class StatusCorsPolicyProvider(IOptions<CorsOptions> options, IServiceProvider services, IConfiguration configuration) : ICorsPolicyProvider
{
    public const string PolicyName = "StatusDomains";

    private readonly DefaultCorsPolicyProvider fallback = new(options);

    public async Task<CorsPolicy?> GetPolicyAsync(HttpContext context, string? policyName)
    {
        if (policyName != PolicyName)
        {
            return await fallback.GetPolicyAsync(context, policyName);
        }

        var builder = new CorsPolicyBuilder()
            .WithOrigins(configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
            .AllowAnyMethod()
            .AllowAnyHeader();

        var origin = context.Request.Headers.Origin.ToString();
        if (Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && origin == uri.GetLeftPart(UriPartial.Authority))
        {
            var status = services.GetRequiredService<IPublicStatusService>();
            if (await status.SlugForDomainAsync(uri.Host, context.RequestAborted) is not null)
            {
                builder.WithOrigins(origin);
            }
        }

        return builder.Build();
    }
}
