using System.ComponentModel;
using System.Net.Http.Headers;
using Flare.Cli.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;

namespace Flare.Cli.Commands;

/// <summary>
/// `flare mcp` - runs a Model Context Protocol server over stdio so AI clients (Claude
/// Code, Cursor, VS Code) can query Flare. The client launches this process and talks
/// JSON-RPC over stdin/stdout, so NOTHING else may write to stdout: logging is routed to
/// stderr and no AnsiConsole output happens on this path. Tools live in
/// <see cref="FlareMcpTools"/> and reuse the same wire DTOs the other commands use.
/// </summary>
internal sealed class McpCommand : AsyncCommand<McpCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
        [CommandOption("--api-url <URL>")]
        [Description("Query a Flare.Api at this base URL (e.g. https://flare.example.com) instead of the local standing instance. Needs --token (or FLARE_API_TOKEN) when that API requires sign-in.")]
        public string? ApiUrl { get; init; }

        [CommandOption("--token <PAT>")]
        [Description("Personal access token (flr_pat_...) sent as a Bearer header. Defaults to the FLARE_API_TOKEN environment variable. Never needed for the local standing instance on loopback.")]
        public string? Token { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        Uri apiBase;
        var ready = true;
        if (!string.IsNullOrWhiteSpace(settings.ApiUrl))
        {
            if (!Uri.TryCreate(settings.ApiUrl, UriKind.Absolute, out var parsed) || parsed.Scheme is not ("http" or "https"))
            {
                Console.Error.WriteLine($"--api-url '{settings.ApiUrl}' is not a valid http(s) URL.");
                return 1;
            }

            apiBase = parsed;
        }
        else
        {
            var instance = FlareHome.ResolveTarget(settings.InstanceName);
            ready = instance.IsInitialized;
            apiBase = new Uri($"http://localhost:{instance.ReadEnvValue("FLARE_API_PORT", "8080")}");
        }

        var http = new HttpClient { BaseAddress = apiBase };
        var token = settings.Token ?? Environment.GetEnvironmentVariable("FLARE_API_TOKEN");
        if (!string.IsNullOrWhiteSpace(token))
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        }

        var builder = Host.CreateEmptyApplicationBuilder(settings: null);
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Services.AddSingleton(new FlareApiClient(http, ready));
        builder.Services
            .AddMcpServer(o => o.ServerInfo = new() { Name = "flare", Version = typeof(McpCommand).Assembly.GetName().Version?.ToString() ?? "0" })
            .WithStdioServerTransport()
            .WithTools<FlareMcpTools>();

        await builder.Build().RunAsync(cancellationToken);
        return 0;
    }
}

/// <summary>Resolved API endpoint for the targeted instance, injected into the tools.</summary>
internal sealed record FlareApiClient(HttpClient Http, bool InstanceInitialized);
