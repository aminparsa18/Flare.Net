using System.ComponentModel;
using System.Net.Http.Json;
using System.Text.Json;
using Flare.Cli.Internal;
using Flare.Mcp;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Flare.Cli.Commands;

/// <summary>
/// <c>flare erasure preview/run/status/export</c> - data-subject erasure and export via Flare.Api's
/// <c>/api/erasure</c> (docs-internal/adr/0198-data-subject-erasure-export.md). Same surface as the
/// API; an erasure is applied by ClickHouse in the background, so <c>run</c> waits for it by default.
/// </summary>
internal sealed record ErasureIdentifierWire(string Key, string Value);

internal sealed record ErasureTableCountWire(string Table, long Rows);

internal sealed record ErasurePreviewWire(List<ErasureTableCountWire> Tables, long TotalRows, int SessionCount, bool SessionsTruncated);

internal sealed record ErasureTableProgressWire(string Table, long Rows, string Status, long PartsToDo, string? Error);

internal sealed record ErasureOperationWire(Guid TransactionId, string Status, string? Error, List<string> IdentifierKeys, string SubjectHash, string RequestedBy, DateTimeOffset RequestedAt, List<ErasureTableProgressWire> Tables);

internal sealed record ErasureListWire(List<ErasureOperationWire> Operations);

internal sealed record StartErasureWire(Guid TransactionId);

internal static class ErasureClient
{
    /// <summary>Parses repeated <c>KEY=VALUE</c> options (the value may itself contain <c>=</c>).</summary>
    public static bool TryParseIdentifiers(string[] ids, out List<ErasureIdentifierWire> identifiers)
    {
        identifiers = [];
        foreach (var text in ids)
        {
            var eq = text.IndexOf('=');
            if (eq <= 0 || eq == text.Length - 1)
            {
                return false;
            }

            identifiers.Add(new ErasureIdentifierWire(text[..eq], text[(eq + 1)..]));
        }

        return identifiers.Count > 0;
    }

    public static string Colour(string status) => status switch
    {
        "success" => "[green]success[/]",
        "failed" => "[red]failed[/]",
        "running" => "[yellow]running[/]",
        _ => $"[grey]{status}[/]",
    };

    public static void WriteOperation(ErasureOperationWire operation)
    {
        AnsiConsole.MarkupLine($"{operation.TransactionId}  {Colour(operation.Status)}  [grey]keys: {Markup.Escape(string.Join(", ", operation.IdentifierKeys))}, subject {operation.SubjectHash}[/]");
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Table");
        table.AddColumn(new TableColumn("Rows").RightAligned());
        table.AddColumn("Status");
        table.AddColumn(new TableColumn("Parts left").RightAligned());
        foreach (var t in operation.Tables)
        {
            table.AddRow(t.Table, t.Rows.ToString("N0"), Colour(t.Status) + (t.Error is null ? "" : " " + Markup.Escape(t.Error)), t.PartsToDo.ToString("N0"));
        }

        AnsiConsole.Write(table);
        if (operation.Error is not null)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(operation.Error)}");
        }
    }

    public static async Task<string?> ProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            return problem.TryGetProperty("detail", out var detail) ? detail.GetString() : null;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return null;
        }
    }
}

internal class ErasureSubjectSettings : InstanceSettings
{
    [CommandOption("--id <KEY=VALUE>")]
    [Description("An identifier of the data subject, e.g. user.id=42, user.email=a@b.c or client.address=10.0.0.1. Repeatable; a row matches if any identifier does.")]
    public string[] Ids { get; init; } = [];
}

internal sealed class ErasurePreviewCommand : AsyncCommand<ErasureSubjectSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, ErasureSubjectSettings settings, CancellationToken cancellationToken)
    {
        var instance = FlareHome.ResolveTarget(settings.InstanceName);
        if (!instance.IsInitialized)
        {
            AnsiConsole.MarkupLine($"[grey]Not initialized yet - run `{instance.StartHint}` first.[/]");
            return 1;
        }

        if (!ErasureClient.TryParseIdentifiers(settings.Ids, out var identifiers))
        {
            AnsiConsole.MarkupLine("[red]✗[/] Pass at least one --id KEY=VALUE.");
            return 1;
        }

        using var http = RetentionClient.Create(instance);
        using var response = await http.PostAsJsonAsync("/api/erasure/preview", new { identifiers }, WireJsonOptions.Instance, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(await ErasureClient.ProblemAsync(response, cancellationToken) ?? $"{(int)response.StatusCode} {response.ReasonPhrase}")}");
            return 1;
        }

        var preview = await response.Content.ReadFromJsonAsync<ErasurePreviewWire>(WireJsonOptions.Instance, cancellationToken);
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Table");
        table.AddColumn(new TableColumn("Matching rows").RightAligned());
        foreach (var t in preview!.Tables)
        {
            table.AddRow(t.Table, t.Rows.ToString("N0"));
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"Total [bold]{preview.TotalRows:N0}[/] rows across {preview.SessionCount:N0} session(s).");
        if (preview.SessionsTruncated)
        {
            AnsiConsole.MarkupLine("[yellow]![/] More sessions matched than are followed; some screenshots and feedback are not covered.");
        }

        return 0;
    }
}

internal sealed class ErasureRunCommand : AsyncCommand<ErasureRunCommand.Settings>
{
    internal sealed class Settings : ErasureSubjectSettings
    {
        [CommandOption("--confirm <VALUE>")]
        [Description("Must equal the first --id value. Deletion is permanent.")]
        public string? Confirm { get; init; }

        [CommandOption("--no-wait")]
        [Description("Return as soon as the API accepts the request instead of waiting for ClickHouse to finish the deletes.")]
        public bool NoWait { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var instance = FlareHome.ResolveTarget(settings.InstanceName);
        if (!instance.IsInitialized)
        {
            AnsiConsole.MarkupLine($"[grey]Not initialized yet - run `{instance.StartHint}` first.[/]");
            return 1;
        }

        if (!ErasureClient.TryParseIdentifiers(settings.Ids, out var identifiers))
        {
            AnsiConsole.MarkupLine("[red]✗[/] Pass at least one --id KEY=VALUE.");
            return 1;
        }

        using var http = RetentionClient.Create(instance);
        using var response = await http.PostAsJsonAsync("/api/erasure", new { identifiers, confirm = settings.Confirm }, WireJsonOptions.Instance, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await ErasureClient.ProblemAsync(response, cancellationToken) ?? $"{(int)response.StatusCode} {response.ReasonPhrase}";
            AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(detail)}{(settings.Confirm is null ? " Pass --confirm with the first --id value (run `flare erasure preview` first)." : "")}");
            return 1;
        }

        var started = await response.Content.ReadFromJsonAsync<StartErasureWire>(WireJsonOptions.Instance, cancellationToken);
        AnsiConsole.MarkupLine($"[green]✓[/] Erasure accepted: {started!.TransactionId}");
        if (settings.NoWait)
        {
            return 0;
        }

        return await ErasureStatusCommand.WaitAsync(http, started.TransactionId, cancellationToken);
    }
}

internal sealed class ErasureStatusCommand : AsyncCommand<ErasureStatusCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
        [CommandArgument(0, "[ID]")]
        [Description("Transaction id; omit to list recent requests.")]
        public Guid? Id { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var instance = FlareHome.ResolveTarget(settings.InstanceName);
        if (!instance.IsInitialized)
        {
            AnsiConsole.MarkupLine($"[grey]Not initialized yet - run `{instance.StartHint}` first.[/]");
            return 1;
        }

        using var http = RetentionClient.Create(instance);
        if (settings.Id is { } id)
        {
            var operation = await http.GetFromJsonAsync<ErasureOperationWire>($"/api/erasure/{id}", WireJsonOptions.Instance, cancellationToken);
            ErasureClient.WriteOperation(operation!);
            return operation!.Status == "failed" ? 1 : 0;
        }

        var list = await http.GetFromJsonAsync<ErasureListWire>("/api/erasure", WireJsonOptions.Instance, cancellationToken);
        foreach (var operation in list?.Operations ?? [])
        {
            ErasureClient.WriteOperation(operation);
        }

        return 0;
    }

    /// <summary>Polls until every table's mutation finished or one failed. Deletes can take a while on large tables, so there is no timeout; Ctrl+C leaves the request running.</summary>
    public static async Task<int> WaitAsync(HttpClient http, Guid id, CancellationToken cancellationToken)
    {
        while (true)
        {
            var operation = await http.GetFromJsonAsync<ErasureOperationWire>($"/api/erasure/{id}", WireJsonOptions.Instance, cancellationToken);
            if (operation!.Status is "success" or "failed")
            {
                ErasureClient.WriteOperation(operation);
                return operation.Status == "success" ? 0 : 1;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }
}

internal sealed class ErasureExportCommand : AsyncCommand<ErasureExportCommand.Settings>
{
    internal sealed class Settings : ErasureSubjectSettings
    {
        [CommandOption("--output <FILE>")]
        [Description("Write the NDJSON export to FILE instead of stdout.")]
        public string? Output { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var instance = FlareHome.ResolveTarget(settings.InstanceName);
        if (!instance.IsInitialized)
        {
            AnsiConsole.MarkupLine($"[grey]Not initialized yet - run `{instance.StartHint}` first.[/]");
            return 1;
        }

        if (!ErasureClient.TryParseIdentifiers(settings.Ids, out var identifiers))
        {
            AnsiConsole.MarkupLine("[red]✗[/] Pass at least one --id KEY=VALUE.");
            return 1;
        }

        using var http = RetentionClient.Create(instance);
        http.Timeout = Timeout.InfiniteTimeSpan;
        using var response = await http.PostAsJsonAsync("/api/erasure/export", new { identifiers }, WireJsonOptions.Instance, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await Console.Error.WriteLineAsync($"Export failed: {await ErasureClient.ProblemAsync(response, cancellationToken) ?? $"{(int)response.StatusCode} {response.ReasonPhrase}"}");
            return 1;
        }

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = settings.Output is { } path ? File.Create(path) : Console.OpenStandardOutput();
        await body.CopyToAsync(output, cancellationToken);
        if (settings.Output is not null)
        {
            await Console.Error.WriteLineAsync($"Wrote {settings.Output}");
        }

        return 0;
    }
}
