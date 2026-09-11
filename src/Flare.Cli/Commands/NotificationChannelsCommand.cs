using System.ComponentModel;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flare.Cli.Internal;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Flare.Cli.Commands;

/// <summary>
/// <c>flare notification-channels list/create/update/delete/send-test</c> - CRUD for saved
/// <c>NotificationChannel</c>s via Flare.Api's <c>/api/notification-channels</c>
/// (<c>src/Flare.Api/Endpoints/NotificationChannelEndpoints.cs</c>). This is the CLI
/// management surface ADR-0021 deliberately deferred when reusable channels shipped (see
/// <c>docs-internal/adr/0021-reusable-notification-channels.md</c>'s Alternatives section,
/// and <see cref="AlertRuleWire.ChannelIds"/>'s own doc comment) - previously
/// <c>AlertsListCommand</c>'s rule summary could only show a referenced channel count, never
/// a channel's name/destination, because nothing here could look one up.
/// </summary>
internal sealed class NotificationChannelsListCommand : AsyncCommand<NotificationChannelsListCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var instance = FlareHome.ResolveTarget(settings.InstanceName);

        if (!instance.IsInitialized)
        {
            AnsiConsole.MarkupLine($"[grey]Not initialized yet - run `{instance.StartHint}` first.[/]");
            return 1;
        }

        var port = instance.ReadEnvValue("FLARE_API_PORT", "8080");
        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };

        NotificationChannelListResponseWire? response;
        try
        {
            using var httpResponse = await http.GetAsync("/api/notification-channels", cancellationToken);

            if (!httpResponse.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] GET /api/notification-channels failed: {(int)httpResponse.StatusCode} {httpResponse.ReasonPhrase}");
                return 1;
            }

            response = await httpResponse.Content.ReadFromJsonAsync<NotificationChannelListResponseWire>(WireJsonOptions.Instance, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach the API on localhost:{port} - is `api` running? Check `flare status`.");
            return 1;
        }

        var channels = response?.Channels ?? [];
        if (channels.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No notification channels configured.[/]");
            return 0;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Name");
        table.AddColumn("Type");
        table.AddColumn("Destination");
        table.AddColumn("Id");

        foreach (var channel in channels)
        {
            table.AddRow(
                Markup.Escape(channel.Name),
                channel.Type,
                Markup.Escape(DestinationSummary(channel)),
                $"[grey]{channel.Id}[/]");
        }

        AnsiConsole.Write(table);
        return 0;
    }

    /// <summary>
    /// Redacted one-line destination summary - never a raw secret in full. Mirrors the
    /// dashboard's own <c>NotificationChannelTable.svelte</c> <c>destinationSummary</c>
    /// exactly (webhook URL/email address in full, Telegram as chat id only - never the bot
    /// token, PagerDuty routing key truncated to its first 6 characters) so the same
    /// channel reads the same way from either surface.
    /// </summary>
    private static string DestinationSummary(NotificationChannelWire channel) => channel.Type switch
    {
        "Webhook" => channel.WebhookUrl,
        "Telegram" => string.IsNullOrEmpty(channel.TelegramChatId) ? "" : $"chat {channel.TelegramChatId}",
        "Email" => channel.EmailTo,
        "PagerDuty" => string.IsNullOrEmpty(channel.PagerDutyRoutingKey) ? "" : $"{channel.PagerDutyRoutingKey[..Math.Min(6, channel.PagerDutyRoutingKey.Length)]}…",
        _ => "",
    };
}

/// <summary>
/// <c>flare notification-channels create &lt;NAME&gt; --type &lt;TYPE&gt; ...</c> via
/// Flare.Api's <c>POST /api/notification-channels</c>. <c>--type</c> plus exactly the
/// matching destination option(s) are required - same
/// <c>NotificationChannelRequest.ValidateDestination()</c> exclusivity the API itself
/// enforces (see that method's doc comment); this command doesn't duplicate the rule
/// client-side, it just surfaces the API's own 400 detail when it's violated.
/// </summary>
internal sealed class NotificationChannelsCreateCommand : AsyncCommand<NotificationChannelsCreateCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
        [CommandArgument(0, "<NAME>")]
        [Description("A label for this channel, e.g. 'on-call-pagerduty' or 'eng-slack'.")]
        public required string Name { get; init; }

        [CommandOption("--type <TYPE>")]
        [Description("Destination type: webhook, telegram, email, or pagerduty. Required.")]
        public string? Type { get; init; }

        [CommandOption("--description <DESCRIPTION>")]
        [Description("Optional free-text note.")]
        public string? Description { get; init; }

        [CommandOption("--webhook-url <URL>")]
        [Description("Required when --type webhook.")]
        public string? WebhookUrl { get; init; }

        [CommandOption("--telegram-bot-token <TOKEN>")]
        [Description("Required when --type telegram.")]
        public string? TelegramBotToken { get; init; }

        [CommandOption("--telegram-chat-id <CHAT_ID>")]
        [Description("Required when --type telegram.")]
        public string? TelegramChatId { get; init; }

        [CommandOption("--email-to <EMAIL>")]
        [Description("Required when --type email.")]
        public string? EmailTo { get; init; }

        [CommandOption("--pagerduty-routing-key <KEY>")]
        [Description("Required when --type pagerduty.")]
        public string? PagerDutyRoutingKey { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var instance = FlareHome.ResolveTarget(settings.InstanceName);

        if (!instance.IsInitialized)
        {
            AnsiConsole.MarkupLine($"[grey]Not initialized yet - run `{instance.StartHint}` first.[/]");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(settings.Name))
        {
            AnsiConsole.MarkupLine("[red]✗[/] NAME can't be empty.");
            return 1;
        }

        var type = NotificationChannelTypeParsing.Normalize(settings.Type);
        if (type is null)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] --type is required and must be one of: {NotificationChannelTypeParsing.ValidValues}.");
            return 1;
        }

        var port = instance.ReadEnvValue("FLARE_API_PORT", "8080");
        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };

        var request = new NotificationChannelRequestWire
        {
            Name = settings.Name,
            Description = settings.Description,
            Type = type,
            WebhookUrl = settings.WebhookUrl,
            TelegramBotToken = settings.TelegramBotToken,
            TelegramChatId = settings.TelegramChatId,
            EmailTo = settings.EmailTo,
            PagerDutyRoutingKey = settings.PagerDutyRoutingKey,
        };

        NotificationChannelWire? channel;
        try
        {
            using var httpResponse = await http.PostAsJsonAsync("/api/notification-channels", request, WireJsonOptions.Instance, cancellationToken);

            if (!httpResponse.IsSuccessStatusCode)
            {
                var detail = await NotificationChannelTypeParsing.TryReadProblemDetailAsync(httpResponse, cancellationToken);
                AnsiConsole.MarkupLine($"[red]✗[/] POST /api/notification-channels failed: {(int)httpResponse.StatusCode} {Markup.Escape(detail ?? httpResponse.ReasonPhrase ?? "")}");
                return 1;
            }

            channel = await httpResponse.Content.ReadFromJsonAsync<NotificationChannelWire>(WireJsonOptions.Instance, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach the API on localhost:{port} - is `api` running? Check `flare status`.");
            return 1;
        }

        if (channel is null)
        {
            AnsiConsole.MarkupLine("[red]✗[/] Empty response from /api/notification-channels.");
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]✓[/] Created {channel.Type} channel [bold]{Markup.Escape(channel.Name)}[/] (id: {channel.Id}).");
        return 0;
    }
}

/// <summary>
/// <c>flare notification-channels update &lt;ID&gt; [options]</c> via Flare.Api's
/// <c>PUT /api/notification-channels/{id}</c>, which (like the dashboard's own edit dialog)
/// replaces the whole channel rather than patching individual fields. This command fetches
/// the existing channel first and only overrides the fields an option was actually passed
/// for, so e.g. `update &lt;ID&gt; --description "..."` doesn't require re-typing the
/// destination too. Changing `--type` clears the other destination fields unless the
/// matching new one is also passed, so a stale field from the old type can't linger and
/// fail <c>NotificationChannelRequest.ValidateDestination()</c>'s exclusivity check.
/// </summary>
internal sealed class NotificationChannelsUpdateCommand : AsyncCommand<NotificationChannelsUpdateCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
        [CommandArgument(0, "<ID>")]
        [Description("The channel's id (see `flare notification-channels list`).")]
        public required Guid Id { get; init; }

        // Not `--name` - InstanceSettings already claims `-n|--name` for the target
        // instance (see that type's own doc comment); Spectre.Console.Cli refuses two
        // options with the same long name on one command.
        [CommandOption("--rename <NAME>")]
        [Description("New name for the channel.")]
        public string? Rename { get; init; }

        [CommandOption("--type <TYPE>")]
        [Description("Destination type: webhook, telegram, email, or pagerduty.")]
        public string? Type { get; init; }

        [CommandOption("--description <DESCRIPTION>")]
        public string? Description { get; init; }

        [CommandOption("--webhook-url <URL>")]
        public string? WebhookUrl { get; init; }

        [CommandOption("--telegram-bot-token <TOKEN>")]
        public string? TelegramBotToken { get; init; }

        [CommandOption("--telegram-chat-id <CHAT_ID>")]
        public string? TelegramChatId { get; init; }

        [CommandOption("--email-to <EMAIL>")]
        public string? EmailTo { get; init; }

        [CommandOption("--pagerduty-routing-key <KEY>")]
        public string? PagerDutyRoutingKey { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var instance = FlareHome.ResolveTarget(settings.InstanceName);

        if (!instance.IsInitialized)
        {
            AnsiConsole.MarkupLine($"[grey]Not initialized yet - run `{instance.StartHint}` first.[/]");
            return 1;
        }

        string? normalizedType = null;
        if (settings.Type is not null)
        {
            normalizedType = NotificationChannelTypeParsing.Normalize(settings.Type);
            if (normalizedType is null)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] --type must be one of: {NotificationChannelTypeParsing.ValidValues}.");
                return 1;
            }
        }

        var port = instance.ReadEnvValue("FLARE_API_PORT", "8080");
        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };

        NotificationChannelWire? existing;
        try
        {
            using var getResponse = await http.GetAsync($"/api/notification-channels/{settings.Id}", cancellationToken);

            if (getResponse.StatusCode == HttpStatusCode.NotFound)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] No notification channel with id {settings.Id}.");
                return 1;
            }

            if (!getResponse.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] GET /api/notification-channels/{settings.Id} failed: {(int)getResponse.StatusCode} {getResponse.ReasonPhrase}");
                return 1;
            }

            existing = await getResponse.Content.ReadFromJsonAsync<NotificationChannelWire>(WireJsonOptions.Instance, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach the API on localhost:{port} - is `api` running? Check `flare status`.");
            return 1;
        }

        if (existing is null)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Empty response from /api/notification-channels/{settings.Id}.");
            return 1;
        }

        var effectiveType = normalizedType ?? existing.Type;
        var typeChanged = !string.Equals(effectiveType, existing.Type, StringComparison.Ordinal);

        // Carries an existing destination field forward only when its type is unchanged -
        // see this command's own doc comment for why a type change clears the others.
        string? Carry(string? provided, string existingValue) => provided ?? (typeChanged ? null : existingValue);

        var request = new NotificationChannelRequestWire
        {
            Name = settings.Rename ?? existing.Name,
            Description = settings.Description ?? existing.Description,
            Type = effectiveType,
            WebhookUrl = Carry(settings.WebhookUrl, existing.WebhookUrl),
            TelegramBotToken = Carry(settings.TelegramBotToken, existing.TelegramBotToken),
            TelegramChatId = Carry(settings.TelegramChatId, existing.TelegramChatId),
            EmailTo = Carry(settings.EmailTo, existing.EmailTo),
            PagerDutyRoutingKey = Carry(settings.PagerDutyRoutingKey, existing.PagerDutyRoutingKey),
        };

        NotificationChannelWire? updated;
        try
        {
            using var putResponse = await http.PutAsJsonAsync($"/api/notification-channels/{settings.Id}", request, WireJsonOptions.Instance, cancellationToken);

            if (putResponse.StatusCode == HttpStatusCode.NotFound)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] No notification channel with id {settings.Id}.");
                return 1;
            }

            if (!putResponse.IsSuccessStatusCode)
            {
                var detail = await NotificationChannelTypeParsing.TryReadProblemDetailAsync(putResponse, cancellationToken);
                AnsiConsole.MarkupLine($"[red]✗[/] PUT /api/notification-channels/{settings.Id} failed: {(int)putResponse.StatusCode} {Markup.Escape(detail ?? putResponse.ReasonPhrase ?? "")}");
                return 1;
            }

            updated = await putResponse.Content.ReadFromJsonAsync<NotificationChannelWire>(WireJsonOptions.Instance, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach the API on localhost:{port} - is `api` running? Check `flare status`.");
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]✓[/] Updated {updated?.Type ?? effectiveType} channel [bold]{Markup.Escape(updated?.Name ?? request.Name)}[/] (id: {settings.Id}).");
        return 0;
    }
}

/// <summary>
/// <c>flare notification-channels delete &lt;ID&gt;</c> via Flare.Api's
/// <c>DELETE /api/notification-channels/{id}</c>. Fetches the channel first purely to name
/// it in the confirmation prompt - same "confirm before an irreversible remove" caution
/// <see cref="DestroyCommand"/> uses for the whole stack, scaled to one row: an interactive
/// prompt by default, `-y|--yes` to skip it (required on a non-interactive invocation, so a
/// script can't delete a channel by accident with no flag).
/// </summary>
internal sealed class NotificationChannelsDeleteCommand : AsyncCommand<NotificationChannelsDeleteCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
        [CommandArgument(0, "<ID>")]
        [Description("The channel's id (see `flare notification-channels list`).")]
        public required Guid Id { get; init; }

        [CommandOption("-y|--yes")]
        [Description("Skip the interactive confirmation prompt. Required for non-interactive use.")]
        public bool Yes { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var instance = FlareHome.ResolveTarget(settings.InstanceName);

        if (!instance.IsInitialized)
        {
            AnsiConsole.MarkupLine($"[grey]Not initialized yet - run `{instance.StartHint}` first.[/]");
            return 1;
        }

        var port = instance.ReadEnvValue("FLARE_API_PORT", "8080");
        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };

        NotificationChannelWire? existing;
        try
        {
            using var getResponse = await http.GetAsync($"/api/notification-channels/{settings.Id}", cancellationToken);

            if (getResponse.StatusCode == HttpStatusCode.NotFound)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] No notification channel with id {settings.Id}.");
                return 1;
            }

            if (!getResponse.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] GET /api/notification-channels/{settings.Id} failed: {(int)getResponse.StatusCode} {getResponse.ReasonPhrase}");
                return 1;
            }

            existing = await getResponse.Content.ReadFromJsonAsync<NotificationChannelWire>(WireJsonOptions.Instance, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach the API on localhost:{port} - is `api` running? Check `flare status`.");
            return 1;
        }

        var name = existing?.Name ?? settings.Id.ToString();

        if (!settings.Yes)
        {
            if (!AnsiConsole.Profile.Capabilities.Interactive)
            {
                AnsiConsole.MarkupLine("[red]Refusing to delete without --yes on a non-interactive invocation.[/]");
                return 1;
            }

            var confirmed = AnsiConsole.Confirm($"Delete notification channel [bold]{Markup.Escape(name)}[/]? Any alert rule referencing it will silently drop it. Continue?", defaultValue: false);
            if (!confirmed)
            {
                AnsiConsole.MarkupLine("[grey]Aborted - nothing was removed.[/]");
                return 1;
            }
        }

        try
        {
            using var deleteResponse = await http.DeleteAsync($"/api/notification-channels/{settings.Id}", cancellationToken);

            if (deleteResponse.StatusCode == HttpStatusCode.NotFound)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] No notification channel with id {settings.Id}.");
                return 1;
            }

            if (!deleteResponse.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] DELETE /api/notification-channels/{settings.Id} failed: {(int)deleteResponse.StatusCode} {deleteResponse.ReasonPhrase}");
                return 1;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach the API on localhost:{port} - is `api` running? Check `flare status`.");
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]✓[/] Deleted channel [bold]{Markup.Escape(name)}[/].");
        return 0;
    }
}

/// <summary>
/// <c>flare notification-channels send-test &lt;ID&gt;</c> via Flare.Api's
/// <c>POST /api/notification-channels/{id}/send-test</c> - a synthetic test notification
/// through this one channel, independent of any alert rule (see
/// <c>NotificationChannelEndpoints.HandleSendTestAsync</c>'s doc comment). Reuses
/// <see cref="AlertNotificationTestResultWire"/> from <c>AlertsCommand.cs</c> - identical
/// response shape to <see cref="AlertsSendTestCommand"/>'s, no need for its own copy.
/// </summary>
internal sealed class NotificationChannelsSendTestCommand : AsyncCommand<NotificationChannelsSendTestCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
        [CommandArgument(0, "<ID>")]
        [Description("The channel's id (see `flare notification-channels list`).")]
        public required Guid Id { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var instance = FlareHome.ResolveTarget(settings.InstanceName);

        if (!instance.IsInitialized)
        {
            AnsiConsole.MarkupLine($"[grey]Not initialized yet - run `{instance.StartHint}` first.[/]");
            return 1;
        }

        var port = instance.ReadEnvValue("FLARE_API_PORT", "8080");
        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };

        AlertNotificationTestResultWire? result;
        try
        {
            using var httpResponse = await http.PostAsync($"/api/notification-channels/{settings.Id}/send-test", content: null, cancellationToken);

            if (httpResponse.StatusCode == HttpStatusCode.NotFound)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] No notification channel with id {settings.Id}.");
                return 1;
            }

            if (!httpResponse.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] POST /api/notification-channels/{settings.Id}/send-test failed: {(int)httpResponse.StatusCode} {httpResponse.ReasonPhrase}");
                return 1;
            }

            result = await httpResponse.Content.ReadFromJsonAsync<AlertNotificationTestResultWire>(WireJsonOptions.Instance, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach the API on localhost:{port} - is `api` running? Check `flare status`.");
            return 1;
        }

        if (result is null)
        {
            AnsiConsole.MarkupLine("[red]✗[/] Empty response from /api/notification-channels/{id}/send-test.");
            return 1;
        }

        if (result.Success)
        {
            AnsiConsole.MarkupLine("[green]✓[/] Test notification sent.");
            return 0;
        }

        AnsiConsole.MarkupLine($"[red]✗[/] Send failed: {Markup.Escape(result.Error)}");
        return 1;
    }
}

/// <summary>
/// <c>--type</c> parsing shared by create/update, plus reading a validation error's
/// <c>detail</c> back out of the API's <see cref="Results.Problem"/> body (an
/// <c>application/problem+json</c> object) so e.g. <c>ValidateDestination()</c>'s message
/// reaches the terminal instead of a bare "400 Bad Request".
/// </summary>
internal static class NotificationChannelTypeParsing
{
    public const string ValidValues = "webhook, telegram, email, pagerduty";

    public static string? Normalize(string? type) => type?.Trim().ToLowerInvariant() switch
    {
        "webhook" => "Webhook",
        "telegram" => "Telegram",
        "email" => "Email",
        "pagerduty" or "pager-duty" => "PagerDuty",
        _ => null,
    };

    public static async Task<string?> TryReadProblemDetailAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return document.RootElement.TryGetProperty("detail", out var detail) ? detail.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

// ---- Wire DTOs - hand-mirror of Flare.Api's Model/NotificationChannelModels.cs (see
// NotificationChannelsJsonContext's camelCase-properties convention). Type is a plain
// string, not a C# enum - matching TracesCommand/SearchCommand's existing
// strings-not-enums convention for wire DTOs (avoids needing a JsonStringEnumConverter on
// WireJsonOptions). -------------

internal sealed class NotificationChannelWire
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = "";

    /// <summary>"Webhook" | "Telegram" | "Email" | "PagerDuty".</summary>
    public required string Type { get; init; }

    public string WebhookUrl { get; init; } = "";

    public string TelegramBotToken { get; init; } = "";

    public string TelegramChatId { get; init; } = "";

    public string EmailTo { get; init; } = "";

    public string PagerDutyRoutingKey { get; init; } = "";

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}

internal sealed class NotificationChannelListResponseWire
{
    public List<NotificationChannelWire> Channels { get; init; } = [];
}

internal sealed class NotificationChannelRequestWire
{
    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>"Webhook" | "Telegram" | "Email" | "PagerDuty".</summary>
    public required string Type { get; init; }

    public string? WebhookUrl { get; init; }

    public string? TelegramBotToken { get; init; }

    public string? TelegramChatId { get; init; }

    public string? EmailTo { get; init; }

    public string? PagerDutyRoutingKey { get; init; }
}
