using System.Reflection;

namespace Flare.Cli.Internal;

/// <summary>
/// Resolves and materializes <c>~/.flare/</c> - the standing state directory a global
/// `flare` install lives in, independent of any particular working directory or repo
/// checkout. Resolved via <see cref="Environment.SpecialFolder.UserProfile"/> (not
/// ApplicationData/LocalApplicationData) deliberately: this is somewhere a user may
/// reasonably want to find and hand-edit (e.g. <c>.env</c>), so it lives next to the
/// other dotfile-style tool homes (<c>~/.aws</c>, <c>~/.docker</c>), not buried in a
/// platform-specific app-data location.
/// </summary>
internal static class FlareHome
{
    private const string ComposeResourceName = "Flare.Cli.Templates.docker-compose.flare.yml";
    private const string EnvTemplateResourceName = "Flare.Cli.Templates.env.template";
    private const string ClickHouseInitResourcePrefix = "Flare.Cli.Templates.ClickHouseInit.";

    public static string Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".flare");

    public static string ComposeFilePath => Path.Combine(Directory, "docker-compose.yml");

    public static string EnvFilePath => Path.Combine(Directory, ".env");

    public static string ClickHouseInitDirectory => Path.Combine(Directory, "db", "clickhouse");

    public static string StateFilePath => Path.Combine(Directory, "state.json");

    /// <summary>
    /// True once a prior `flare start` has materialized the files this tool needs to
    /// operate. Deliberately checks the compose file + env file only, not state.json -
    /// state.json is metadata (last-pulled digests), not a precondition for running.
    /// </summary>
    public static bool IsInitialized => File.Exists(ComposeFilePath) && File.Exists(EnvFilePath);

    /// <summary>
    /// Writes the compose file, a fresh randomly-keyed <c>.env</c>, and the ClickHouse
    /// init scripts from this assembly's embedded resources - but only if
    /// <see cref="IsInitialized"/> is false. Never overwrites an existing
    /// docker-compose.yml/.env in place: a re-run of `flare start` against an
    /// already-initialized home must not clobber local edits or rotate credentials out
    /// from under an already-provisioned ClickHouse/identity store.
    /// </summary>
    public static void EnsureInitialized()
    {
        System.IO.Directory.CreateDirectory(Directory);
        System.IO.Directory.CreateDirectory(ClickHouseInitDirectory);

        var assembly = typeof(FlareHome).Assembly;

        if (!File.Exists(ComposeFilePath))
        {
            File.WriteAllText(ComposeFilePath, ReadEmbeddedResource(assembly, ComposeResourceName));
        }

        if (!File.Exists(EnvFilePath))
        {
            var template = ReadEmbeddedResource(assembly, EnvTemplateResourceName);
            File.WriteAllText(EnvFilePath, EnvGenerator.RenderEnvTemplate(template));
        }

        // Cheap and idempotent to re-extract every run (unlike the compose/env files,
        // these are never hand-edited and always need to match this CLI version's
        // schema exactly) - so no "only if missing" guard here.
        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(ClickHouseInitResourcePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var fileName = resourceName[ClickHouseInitResourcePrefix.Length..];
            File.WriteAllText(
                Path.Combine(ClickHouseInitDirectory, fileName),
                ReadEmbeddedResource(assembly, resourceName));
        }

        if (!File.Exists(StateFilePath))
        {
            StateMetadata.Save(StateFilePath, new StateMetadata
            {
                ImageTag = "edge",
                CliVersion = assembly.GetName().Version?.ToString() ?? "unknown",
            });
        }
    }

    /// <summary>
    /// Reads a single <c>KEY=value</c> line out of ~/.flare/.env. Deliberately minimal
    /// (no quoting/escaping support) - every value this tool itself writes into the
    /// template is a plain token (port number, tag name, generated alphanumeric secret),
    /// and hand-edits beyond that are the user's own responsibility.
    /// </summary>
    public static string ReadEnvValue(string key, string fallback)
    {
        if (!File.Exists(EnvFilePath))
        {
            return fallback;
        }

        foreach (var line in File.ReadLines(EnvFilePath))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = trimmed.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            if (string.Equals(trimmed[..separatorIndex], key, StringComparison.Ordinal))
            {
                return trimmed[(separatorIndex + 1)..];
            }
        }

        return fallback;
    }

    private static string ReadEmbeddedResource(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' was expected but could not be opened.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
