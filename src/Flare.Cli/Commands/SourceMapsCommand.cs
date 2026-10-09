using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text.Json;
using Flare.Cli.Internal;
using Flare.Mcp.DotnetSymbols;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Flare.Cli.Commands;

/// <summary>Shared target selection for the <c>flare sourcemaps</c> commands: the local instance by default, or any Flare.Api via <c>--url</c> and <c>--token</c> (a CI job's usual case).</summary>
internal class SourceMapsSettings : InstanceSettings
{
    [CommandOption("--url <URL>")]
    [Description("Base URL of Flare.Api, e.g. https://flare.example.com. Default: the local instance.")]
    public string? Url { get; init; }

    [CommandOption("--token <TOKEN>")]
    [Description("A personal access token (flr_pat_...) of an admin. Default: the FLARE_API_TOKEN environment variable.")]
    public string? Token { get; init; }

    [CommandOption("--service <NAME>")]
    [Description("The service.name the browser app reports.")]
    public string? Service { get; init; }

    [CommandOption("--release <VERSION>")]
    [Description("The service.version (or vcs revision) the build reports.")]
    public string? Release { get; init; }

    /// <summary>Creates the client, or returns null after printing why it can't.</summary>
    internal HttpClient? CreateClient()
    {
        Uri baseAddress;
        if (!string.IsNullOrWhiteSpace(Url))
        {
            if (!Uri.TryCreate(Url, UriKind.Absolute, out var parsed) || parsed.Scheme is not ("http" or "https"))
            {
                AnsiConsole.MarkupLine("[red]✗[/] --url must be an http(s) URL.");
                return null;
            }

            baseAddress = parsed;
        }
        else
        {
            var instance = FlareHome.ResolveTarget(InstanceName);
            if (!instance.IsInitialized)
            {
                AnsiConsole.MarkupLine($"[grey]Not initialized yet - run `{instance.StartHint}` first, or pass --url.[/]");
                return null;
            }

            baseAddress = new Uri($"http://localhost:{instance.ReadEnvValue("FLARE_API_PORT", "8080")}");
        }

        var http = new HttpClient { BaseAddress = baseAddress, Timeout = TimeSpan.FromMinutes(5) };
        var token = Token ?? Environment.GetEnvironmentVariable("FLARE_API_TOKEN");
        if (!string.IsNullOrWhiteSpace(token))
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return http;
    }
}

/// <summary>
/// <c>flare sourcemaps upload &lt;PATH&gt;...</c> - uploads <c>.map</c> files (ADR-0152) via
/// <c>PUT /api/source-maps</c>. A directory is searched recursively; each map's bundle path is its
/// location relative to that directory with <c>.map</c> removed (<c>dist/assets/app.js.map</c> uploaded from
/// <c>dist</c> becomes <c>assets/app.js</c>), which a stack frame's URL path is matched against by suffix.
/// </summary>
internal sealed class SourceMapsUploadCommand : AsyncCommand<SourceMapsUploadCommand.Settings>
{
    internal sealed class Settings : SourceMapsSettings
    {
        [CommandArgument(0, "<PATH>")]
        [Description("A .map file or a directory containing them (e.g. your build's dist folder).")]
        public required string[] Paths { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.Service) || string.IsNullOrWhiteSpace(settings.Release))
        {
            AnsiConsole.MarkupLine("[red]✗[/] --service and --release are required.");
            return 1;
        }

        var maps = new List<(string File, string Bundle)>();
        foreach (var path in settings.Paths)
        {
            if (Directory.Exists(path))
            {
                var root = Path.GetFullPath(path);
                maps.AddRange(Directory.EnumerateFiles(root, "*.map", SearchOption.AllDirectories)
                    .Select(f => (f, BundleName(Path.GetRelativePath(root, f)))));
            }
            else if (File.Exists(path))
            {
                maps.Add((path, BundleName(Path.GetFileName(path))));
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(path)} does not exist.");
                return 1;
            }
        }

        if (maps.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]✗[/] No .map files found.");
            return 1;
        }

        using var http = settings.CreateClient();
        if (http is null)
        {
            return 1;
        }

        var failed = 0;
        foreach (var (file, bundle) in maps)
        {
            var query = $"/api/source-maps?service={Uri.EscapeDataString(settings.Service)}&version={Uri.EscapeDataString(settings.Release)}&bundle={Uri.EscapeDataString(bundle)}";
            try
            {
                await using var stream = File.OpenRead(file);
                using var content = new StreamContent(stream);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                using var response = await http.PutAsync(query, content, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    AnsiConsole.MarkupLine($"[green]✓[/] {Markup.Escape(bundle)}");
                    continue;
                }

                failed++;
                AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(bundle)}: {(int)response.StatusCode} {Markup.Escape(await ProblemDetail(response, cancellationToken))}");
            }
            catch (HttpRequestException ex)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach {Markup.Escape(http.BaseAddress!.ToString())}: {Markup.Escape(ex.Message)}");
                return 1;
            }
        }

        AnsiConsole.MarkupLine(failed == 0
            ? $"[green]✓[/] Uploaded {maps.Count} source map(s) for {Markup.Escape(settings.Service)} {Markup.Escape(settings.Release)}."
            : $"[red]✗[/] {failed} of {maps.Count} source map(s) failed.");
        return failed == 0 ? 0 : 1;
    }

    /// <summary><c>assets/app.js.map</c> -&gt; <c>assets/app.js</c>, with forward slashes on every OS.</summary>
    internal static string BundleName(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        return normalized.EndsWith(".map", StringComparison.OrdinalIgnoreCase) ? normalized[..^4] : normalized;
    }

    internal static async Task<string> ProblemDetail(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (doc.RootElement.TryGetProperty("detail", out var detail) && detail.GetString() is { Length: > 0 } text)
            {
                return text;
            }
        }
        catch (JsonException)
        {
            // fall through to the reason phrase
        }

        return response.ReasonPhrase ?? "";
    }
}

/// <summary>
/// <c>flare sourcemaps upload-dotnet &lt;PATH&gt;...</c> - for each assembly (a dll, or a directory searched
/// recursively) builds a compact symbols file from the dll and its portable PDB (sibling or embedded) and
/// uploads it as <c>{mvid}.dotnet.json</c>, which Mono-style frames of trimmed/AOT builds are matched by (ADR-0168).
/// Assemblies without a usable PDB are skipped with a note; only a failed upload makes the command fail.
/// </summary>
internal sealed class SourceMapsUploadDotnetCommand : AsyncCommand<SourceMapsUploadDotnetCommand.Settings>
{
    internal sealed class Settings : SourceMapsSettings
    {
        [CommandArgument(0, "<PATH>")]
        [Description("An assembly (.dll) or a directory containing them (e.g. your obj/Release/net10.0-android folder).")]
        public required string[] Paths { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.Service) || string.IsNullOrWhiteSpace(settings.Release))
        {
            AnsiConsole.MarkupLine("[red]✗[/] --service and --release are required.");
            return 1;
        }

        var dlls = new List<string>();
        foreach (var path in settings.Paths)
        {
            if (Directory.Exists(path))
            {
                dlls.AddRange(Directory.EnumerateFiles(Path.GetFullPath(path), "*.dll", SearchOption.AllDirectories));
            }
            else if (File.Exists(path))
            {
                dlls.Add(path);
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(path)} does not exist.");
                return 1;
            }
        }

        using var http = settings.CreateClient();
        if (http is null)
        {
            return 1;
        }

        int uploaded = 0, failed = 0;
        var seen = new HashSet<Guid>();
        foreach (var dll in dlls)
        {
            (byte[]? json, Guid mvid, string? error) built;
            try
            {
                built = DotnetSymbolsBuilder.Build(dll);
            }
            catch (Exception ex) when (ex is BadImageFormatException or IOException or InvalidOperationException)
            {
                continue; // native or unreadable file next to the managed ones
            }

            if (built.json is null)
            {
                if (built.mvid != default)
                {
                    AnsiConsole.MarkupLine($"[grey]- {Markup.Escape(Path.GetFileName(dll))}: skipped, {Markup.Escape(built.error ?? "")}[/]");
                }

                continue;
            }

            if (!seen.Add(built.mvid))
            {
                continue;
            }

            var bundle = DotnetSymbols.BundleName(built.mvid);
            var query = $"/api/source-maps?service={Uri.EscapeDataString(settings.Service)}&version={Uri.EscapeDataString(settings.Release)}&bundle={Uri.EscapeDataString(bundle)}";
            try
            {
                using var content = new ByteArrayContent(built.json);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                using var response = await http.PutAsync(query, content, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    uploaded++;
                    AnsiConsole.MarkupLine($"[green]✓[/] {Markup.Escape(Path.GetFileName(dll))} ({built.mvid:N})");
                    continue;
                }

                failed++;
                AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(Path.GetFileName(dll))}: {(int)response.StatusCode} {Markup.Escape(await SourceMapsUploadCommand.ProblemDetail(response, cancellationToken))}");
            }
            catch (HttpRequestException ex)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach {Markup.Escape(http.BaseAddress!.ToString())}: {Markup.Escape(ex.Message)}");
                return 1;
            }
        }

        if (uploaded == 0 && failed == 0)
        {
            AnsiConsole.MarkupLine("[red]✗[/] No assemblies with a portable PDB found.");
            return 1;
        }

        AnsiConsole.MarkupLine(failed == 0
            ? $"[green]✓[/] Uploaded symbols for {uploaded} assembl{(uploaded == 1 ? "y" : "ies")} ({Markup.Escape(settings.Service)} {Markup.Escape(settings.Release)})."
            : $"[red]✗[/] {failed} upload(s) failed.");
        return failed == 0 ? 0 : 1;
    }
}

/// <summary>
/// <c>flare sourcemaps upload-native &lt;DSYM&gt;...</c> - for each <c>.dSYM</c> bundle (or its DWARF file) builds a
/// function-to-line table and uploads it as <c>{uuid}.native.json</c>, which Native AOT frames of that release
/// are looked up in (ADR-0168). Run it on the <c>.dSYM</c> next to the published binary.
/// </summary>
internal sealed class SourceMapsUploadNativeCommand : AsyncCommand<SourceMapsUploadNativeCommand.Settings>
{
    internal sealed class Settings : SourceMapsSettings
    {
        [CommandArgument(0, "<DSYM>")]
        [Description("A .dSYM bundle (or the DWARF file inside it) produced by `dotnet publish` with PublishAot.")]
        public required string[] Paths { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.Service) || string.IsNullOrWhiteSpace(settings.Release))
        {
            AnsiConsole.MarkupLine("[red]✗[/] --service and --release are required.");
            return 1;
        }

        using var http = settings.CreateClient();
        if (http is null)
        {
            return 1;
        }

        var failed = 0;
        foreach (var path in settings.Paths)
        {
            if (!Directory.Exists(path) && !File.Exists(path))
            {
                AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(path)} does not exist.");
                return 1;
            }

            var (json, uuid, error) = NativeSymbolsBuilder.Build(path);
            if (json is null || uuid is null)
            {
                failed++;
                AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(path)}: {Markup.Escape(error ?? "no symbols")}");
                continue;
            }

            var bundle = NativeSymbols.BundleName(uuid);
            var query = $"/api/source-maps?service={Uri.EscapeDataString(settings.Service)}&version={Uri.EscapeDataString(settings.Release)}&bundle={Uri.EscapeDataString(bundle)}";
            try
            {
                using var content = new ByteArrayContent(json);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                using var response = await http.PutAsync(query, content, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    AnsiConsole.MarkupLine($"[green]✓[/] {Markup.Escape(Path.GetFileName(path.TrimEnd('/')))} ({uuid}, {json.Length / 1024:N0} KB)");
                    continue;
                }

                failed++;
                AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(path)}: {(int)response.StatusCode} {Markup.Escape(await SourceMapsUploadCommand.ProblemDetail(response, cancellationToken))}");
            }
            catch (HttpRequestException ex)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach {Markup.Escape(http.BaseAddress!.ToString())}: {Markup.Escape(ex.Message)}");
                return 1;
            }
        }

        return failed == 0 ? 0 : 1;
    }
}

/// <summary><c>flare sourcemaps list</c> - the uploaded maps, optionally narrowed by --service/--release.</summary>
internal sealed class SourceMapsListCommand : AsyncCommand<SourceMapsSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, SourceMapsSettings settings, CancellationToken cancellationToken)
    {
        using var http = settings.CreateClient();
        if (http is null)
        {
            return 1;
        }

        try
        {
            var query = "/api/source-maps?service=" + Uri.EscapeDataString(settings.Service ?? "") + "&version=" + Uri.EscapeDataString(settings.Release ?? "");
            using var response = await http.GetAsync(query, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] {(int)response.StatusCode} {Markup.Escape(await SourceMapsUploadCommand.ProblemDetail(response, cancellationToken))}");
                return 1;
            }

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var table = new Table().AddColumns("Service", "Release", "Bundle", "Size", "Uploaded");
            foreach (var map in doc.RootElement.GetProperty("maps").EnumerateArray())
            {
                table.AddRow(
                    Markup.Escape(map.GetProperty("serviceName").GetString() ?? ""),
                    Markup.Escape(map.GetProperty("version").GetString() ?? ""),
                    Markup.Escape(map.GetProperty("bundle").GetString() ?? ""),
                    $"{map.GetProperty("sizeBytes").GetInt64() / 1024.0:N0} KB",
                    map.GetProperty("uploadedAt").GetDateTimeOffset().ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
            }

            AnsiConsole.Write(table);
            return 0;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach {Markup.Escape(http.BaseAddress!.ToString())}: {Markup.Escape(ex.Message)}");
            return 1;
        }
    }
}

/// <summary><c>flare sourcemaps delete --service S --release V [--bundle B]</c> - removes a release's maps, or one bundle's.</summary>
internal sealed class SourceMapsDeleteCommand : AsyncCommand<SourceMapsDeleteCommand.Settings>
{
    internal sealed class Settings : SourceMapsSettings
    {
        [CommandOption("--bundle <PATH>")]
        [Description("Delete only this bundle's map (e.g. assets/app.js). Default: every map of the release.")]
        public string? Bundle { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.Service) || string.IsNullOrWhiteSpace(settings.Release))
        {
            AnsiConsole.MarkupLine("[red]✗[/] --service and --release are required.");
            return 1;
        }

        using var http = settings.CreateClient();
        if (http is null)
        {
            return 1;
        }

        try
        {
            var query = $"/api/source-maps?service={Uri.EscapeDataString(settings.Service)}&version={Uri.EscapeDataString(settings.Release)}"
                + (string.IsNullOrWhiteSpace(settings.Bundle) ? "" : "&bundle=" + Uri.EscapeDataString(settings.Bundle));
            using var response = await http.DeleteAsync(query, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] {(int)response.StatusCode} {Markup.Escape(await SourceMapsUploadCommand.ProblemDetail(response, cancellationToken))}");
                return 1;
            }

            AnsiConsole.MarkupLine("[green]✓[/] Deleted.");
            return 0;
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach {Markup.Escape(http.BaseAddress!.ToString())}: {Markup.Escape(ex.Message)}");
            return 1;
        }
    }
}
