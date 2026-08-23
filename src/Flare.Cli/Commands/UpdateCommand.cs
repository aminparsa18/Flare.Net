using System.ComponentModel;
using Flare.Cli.Internal;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Flare.Cli.Commands;

/// <summary>
/// Re-pulls whatever <c>FLARE_IMAGE_TAG</c> is currently pinned in <c>~/.flare/.env</c>
/// and recreates any container whose image actually changed - deliberately does NOT
/// float onto <c>edge</c>/<c>latest</c> or auto-discover newer Docker Hub tags on its
/// own (see docs/cli.md's Image tag policy: pinning is meant to keep a given
/// <c>Flare.Cli</c> version on the same image forever, since only this CLI's own author
/// knows which newer Flare releases have actually been tested against it). <c>--tag</c>
/// is the deliberate escape hatch for moving an existing install onto a newer pin
/// WITHOUT that auto-discovery - it's the CLI-native equivalent of hand-editing
/// <c>FLARE_IMAGE_TAG</c> in <c>.env</c> yourself, nothing more.
/// </summary>
internal sealed class UpdateCommand : AsyncCommand<UpdateCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
        [CommandOption("--tag <TAG>")]
        [Description("Move this install onto a different FLARE_IMAGE_TAG before pulling - rewrites .env in place. Omit to just re-pull whatever tag is already pinned.")]
        public string? Tag { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var instance = FlareHome.ResolveTarget(settings.InstanceName);

        if (!instance.IsInitialized)
        {
            AnsiConsole.MarkupLine($"[grey]Not initialized yet - run `{instance.StartHint}` first.[/]");
            return 1;
        }

        if (settings.Tag is not null)
        {
            if (string.IsNullOrWhiteSpace(settings.Tag))
            {
                AnsiConsole.MarkupLine("[red]✗[/] --tag can't be blank.");
                return 1;
            }

            var previousTag = instance.ReadEnvValue("FLARE_IMAGE_TAG", "(unset)");
            instance.SetEnvValue("FLARE_IMAGE_TAG", settings.Tag);
            AnsiConsole.MarkupLine($"[grey]Pinned tag: {Markup.Escape(previousTag)} -> {Markup.Escape(settings.Tag)}[/]");
        }

        var profile = FlareHome.ResolveTopology(instance);
        var before = await CaptureImageIdsAsync(instance, profile.PullDiffServices);

        AnsiConsole.MarkupLine("[grey]Pulling latest images for the pinned tag...[/]");
        var pullExitCode = await ComposeRunner.RunStreamedAsync(instance, ["pull"]);
        if (pullExitCode != 0)
        {
            AnsiConsole.MarkupLine("[red]✗[/] `docker compose pull` failed - see the output above.");
            return 1;
        }

        // Recreates any container whose image digest actually changed; compose no-ops
        // the rest. Never touches volumes - data persists across update exactly like it
        // does across stop/start.
        var upExitCode = await ComposeRunner.RunStreamedAsync(instance, ["up", "-d"]);
        if (upExitCode != 0)
        {
            AnsiConsole.MarkupLine("[red]✗[/] `docker compose up -d` failed - see the output above.");
            return 1;
        }

        var after = await CaptureImageIdsAsync(instance, profile.PullDiffServices);

        var state = StateMetadata.Load(instance.StateFilePath);
        state.LastPulledAt = DateTimeOffset.UtcNow;
        // Keeps state.json's own record of the pinned tag in sync with .env - previously
        // only ever written once at init time, so it went stale the moment anyone
        // hand-edited FLARE_IMAGE_TAG (this update's own --tag included).
        state.ImageTag = instance.ReadEnvValue("FLARE_IMAGE_TAG", state.ImageTag);

        foreach (var service in profile.PullDiffServices)
        {
            var beforeId = before.GetValueOrDefault(service, "(none)");
            var afterId = after.GetValueOrDefault(service, "(unknown)");
            state.LastPulled[service] = afterId;

            AnsiConsole.MarkupLine(beforeId == afterId
                ? $"  [grey]{service}: unchanged ({Short(afterId)})[/]"
                : $"  [green]{service}: {Short(beforeId)} -> {Short(afterId)}[/]");
        }

        StateMetadata.Save(instance.StateFilePath, state);
        AnsiConsole.MarkupLine("[green]✓[/] Update complete.");
        return 0;
    }

    private static async Task<Dictionary<string, string>> CaptureImageIdsAsync(FlareInstance instance, string[] services)
    {
        var ids = new Dictionary<string, string>();
        foreach (var service in services)
        {
            var result = await ComposeRunner.RunCapturedAsync(instance, ["images", "-q", service]);
            ids[service] = result.StandardOutput.Trim();
        }

        return ids;
    }

    private static string Short(string imageId) => imageId.Length > 12 ? imageId[..12] : imageId;
}
