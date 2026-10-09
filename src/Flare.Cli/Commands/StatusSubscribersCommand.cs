using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Flare.Cli.Commands;

/// <summary>
/// <c>flare status-pages subscribers list/remove</c> - the visitor email subscribers of a status page via
/// Flare.Api's <c>/api/status-pages/{id}/subscribers</c> (<c>src/Flare.Api/Endpoints/StatusSubscriptionEndpoints.cs</c>).
/// See <c>docs-internal/adr/0162-status-page-visitor-subscriptions.md</c>.
/// </summary>
internal sealed class StatusSubscribersListCommand : AsyncCommand<StatusSubscribersListCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
        [CommandArgument(0, "<PAGE_ID>")]
        [Description("The page's id (see `flare status-pages list`).")]
        public required Guid PageId { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var (response, exit) = await StatusIncidentCli.SendAsync<StatusSubscriberListResponseWire>(
            settings.InstanceName, HttpMethod.Get, $"/api/status-pages/{settings.PageId}/subscribers", null, cancellationToken);
        if (exit != 0)
        {
            return exit;
        }

        var subscribers = response?.Subscribers ?? [];
        if (subscribers.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No subscribers on this page.[/]");
            return 0;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Email");
        table.AddColumn("Verified");
        table.AddColumn("Components");
        table.AddColumn("Subscribed");
        table.AddColumn("Id");
        foreach (var subscriber in subscribers)
        {
            table.AddRow(
                Markup.Escape(subscriber.Email),
                subscriber.Verified ? "[green]yes[/]" : "[yellow]pending[/]",
                subscriber.Components.Count == 0 ? "all" : $"{subscriber.Components.Count} selected",
                subscriber.CreatedAt.ToString("u"),
                $"[grey]{subscriber.Id}[/]");
        }

        AnsiConsole.Write(table);
        var pending = subscribers.Count(s => !s.Verified);
        AnsiConsole.MarkupLine($"[grey]{subscribers.Count - pending} verified, {pending} pending confirmation. Only verified addresses are mailed.[/]");
        return 0;
    }
}

/// <summary><c>flare status-pages subscribers remove &lt;PAGE_ID&gt; &lt;SUBSCRIBER_ID&gt;</c>.</summary>
internal sealed class StatusSubscribersRemoveCommand : AsyncCommand<StatusSubscribersRemoveCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
        [CommandArgument(0, "<PAGE_ID>")]
        [Description("The page's id (see `flare status-pages list`).")]
        public required Guid PageId { get; init; }

        [CommandArgument(1, "<SUBSCRIBER_ID>")]
        [Description("The subscriber's id (see `flare status-pages subscribers list`).")]
        public required Guid SubscriberId { get; init; }

        [CommandOption("-y|--yes")]
        [Description("Skip the interactive confirmation prompt. Required for non-interactive use.")]
        public bool Yes { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (!settings.Yes)
        {
            if (!AnsiConsole.Profile.Capabilities.Interactive)
            {
                AnsiConsole.MarkupLine("[red]Refusing to remove without --yes on a non-interactive invocation.[/]");
                return 1;
            }

            if (!AnsiConsole.Confirm($"Remove subscriber [bold]{settings.SubscriberId}[/]? They stop receiving incident emails. Continue?", defaultValue: false))
            {
                AnsiConsole.MarkupLine("[grey]Aborted - nothing was removed.[/]");
                return 1;
            }
        }

        var (_, exit) = await StatusIncidentCli.SendAsync<object>(
            settings.InstanceName, HttpMethod.Delete, $"/api/status-pages/{settings.PageId}/subscribers/{settings.SubscriberId}", null, cancellationToken);
        if (exit == 0)
        {
            AnsiConsole.MarkupLine($"[green]✓[/] Removed subscriber {settings.SubscriberId}.");
        }

        return exit;
    }
}

// ---- Wire DTOs - hand-mirror of Flare.Api's Model/StatusPageModels.cs (camelCase JSON). ----

internal sealed class StatusSubscriberWire
{
    public required Guid Id { get; init; }

    public required string Email { get; init; }

    public bool Verified { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Opaque component keys the subscriber picked; empty means every component.</summary>
    public List<Guid> Components { get; init; } = [];
}

internal sealed class StatusSubscriberListResponseWire
{
    public List<StatusSubscriberWire> Subscribers { get; init; } = [];
}
