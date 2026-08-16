using Flare.Cli.Internal;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Flare.Cli.Commands;

internal sealed class UpdateCommand : AsyncCommand
{
    private static readonly string[] Services = ["ingest", "api", "dashboard"];

    protected override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (!FlareHome.IsInitialized)
        {
            AnsiConsole.MarkupLine("[grey]Not initialized yet - run `flare start` first.[/]");
            return 1;
        }

        var before = await CaptureImageIdsAsync();

        AnsiConsole.MarkupLine("[grey]Pulling latest images for the pinned tag...[/]");
        var pullExitCode = await ComposeRunner.RunStreamedAsync(["pull"]);
        if (pullExitCode != 0)
        {
            AnsiConsole.MarkupLine("[red]✗[/] `docker compose pull` failed - see the output above.");
            return 1;
        }

        // Recreates any container whose image digest actually changed; compose no-ops
        // the rest. Never touches volumes - data persists across update exactly like it
        // does across stop/start.
        var upExitCode = await ComposeRunner.RunStreamedAsync(["up", "-d"]);
        if (upExitCode != 0)
        {
            AnsiConsole.MarkupLine("[red]✗[/] `docker compose up -d` failed - see the output above.");
            return 1;
        }

        var after = await CaptureImageIdsAsync();

        var state = StateMetadata.Load(FlareHome.StateFilePath);
        state.LastPulledAt = DateTimeOffset.UtcNow;

        foreach (var service in Services)
        {
            var beforeId = before.GetValueOrDefault(service, "(none)");
            var afterId = after.GetValueOrDefault(service, "(unknown)");
            state.LastPulled[service] = afterId;

            AnsiConsole.MarkupLine(beforeId == afterId
                ? $"  [grey]{service}: unchanged ({Short(afterId)})[/]"
                : $"  [green]{service}: {Short(beforeId)} -> {Short(afterId)}[/]");
        }

        StateMetadata.Save(FlareHome.StateFilePath, state);
        AnsiConsole.MarkupLine("[green]✓[/] Update complete.");
        return 0;
    }

    private static async Task<Dictionary<string, string>> CaptureImageIdsAsync()
    {
        var ids = new Dictionary<string, string>();
        foreach (var service in Services)
        {
            var result = await ComposeRunner.RunCapturedAsync(["images", "-q", service]);
            ids[service] = result.StandardOutput.Trim();
        }

        return ids;
    }

    private static string Short(string imageId) => imageId.Length > 12 ? imageId[..12] : imageId;
}
