using System.Net.Http.Headers;
using Flare.Mcp;

namespace Flare.Api.Endpoints;

/// <summary>
/// <c>/mcp</c> - the same read-only MCP tools <c>flare mcp</c> serves over stdio, hosted as a
/// streamable-HTTP endpoint so a remote or shared Flare needs no local CLI install. Stateless
/// (every POST stands alone), so it works behind a load balancer. The tools are thin clients
/// over Flare.Api's own REST surface: each call re-enters this process over loopback carrying
/// the caller's <c>Authorization</c> header, so the personal access token's user, role and
/// rate limit decide what a tool can see - the MCP layer adds no privilege of its own. Only the
/// Bearer header is forwarded, never the session cookie, so a browser can't be driven
/// cross-site into the tools.
/// </summary>
public static class McpEndpoints
{
    public const string SelfClientName = "mcp-self";

    public static IServiceCollection AddFlareMcp(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddHttpClient(SelfClientName, (sp, client) =>
        {
            client.BaseAddress = ResolveSelfUrl(sp, configuration);
            client.Timeout = TimeSpan.FromSeconds(60);
        });

        services.AddScoped(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(SelfClientName);
            var authorization = sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.Request.Headers.Authorization.ToString();
            if (!string.IsNullOrWhiteSpace(authorization) && AuthenticationHeaderValue.TryParse(authorization, out var header))
            {
                http.DefaultRequestHeaders.Authorization = header;
            }

            return new FlareApiClient(http);
        });

        services
            .AddMcpServer(o => o.ServerInfo = new() { Name = "flare", Version = typeof(McpEndpoints).Assembly.GetName().Version?.ToString() ?? "0" })
            .WithHttpTransport(o => o.Stateless = true)
            .WithTools<FlareMcpTools>();

        return services;
    }

    public static IEndpointConventionBuilder MapFlareMcp(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapMcp("/mcp");

    // Mcp:SelfUrl wins; otherwise the first address Kestrel bound to, with a wildcard host
    // (http://+:8080, http://[::]:8080) swapped for localhost. Resolved lazily - server
    // addresses only exist once the host has started.
    private static Uri ResolveSelfUrl(IServiceProvider sp, IConfiguration configuration)
    {
        if (Uri.TryCreate(configuration["Mcp:SelfUrl"], UriKind.Absolute, out var configured))
        {
            return configured;
        }

        var address = sp.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
            .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()?.Addresses
            .FirstOrDefault(a => a.StartsWith("http", StringComparison.OrdinalIgnoreCase));
        if (address is null)
        {
            return new Uri("http://localhost:8080");
        }

        var normalized = address.Replace("://+", "://localhost").Replace("://*", "://localhost").Replace("://[::]", "://localhost").Replace("://0.0.0.0", "://localhost");
        return Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ? uri : new Uri("http://localhost:8080");
    }
}
