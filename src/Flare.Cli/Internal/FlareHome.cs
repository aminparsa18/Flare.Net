using System.Reflection;

namespace Flare.Cli.Internal;

/// <summary>
/// One resolved Flare instance's file-system footprint and identity - the default
/// instance (rooted directly at <c>~/.flare/</c>, byte-identical to this tool's
/// pre-multi-instance layout) or a named one (rooted at
/// <c>~/.flare/instances/&lt;name&gt;/</c>). Construct only via <see cref="FlareHome.Resolve"/>,
/// which is also where instance-name validation happens.
/// </summary>
internal sealed class FlareInstance
{
    internal FlareInstance(string? name, string directory, string projectName)
    {
        Name = name;
        Directory = directory;
        ProjectName = projectName;
    }

    /// <summary>Null for the default instance; the validated `--name` value otherwise.</summary>
    public string? Name { get; }

    public string Directory { get; }

    /// <summary>Docker Compose `--project-name` - "flare" for the default instance, "flare-&lt;name&gt;" for a named one. The only thing that namespaces containers/network/volumes apart across instances (see ComposeRunner).</summary>
    public string ProjectName { get; }

    public string ComposeFilePath => Path.Combine(Directory, "docker-compose.yml");

    public string EnvFilePath => Path.Combine(Directory, ".env");

    public string ClickHouseInitDirectory => Path.Combine(Directory, "db", "clickhouse");

    public string StateFilePath => Path.Combine(Directory, "state.json");

    /// <summary>
    /// True once a prior `flare start` has materialized the files this tool needs to
    /// operate. Deliberately checks the compose file + env file only, not state.json -
    /// state.json is metadata (last-pulled digests), not a precondition for running.
    /// </summary>
    public bool IsInitialized => File.Exists(ComposeFilePath) && File.Exists(EnvFilePath);

    public string ReadEnvValue(string key, string fallback) => FlareHome.ReadEnvValueFrom(EnvFilePath, key, fallback);

    public void SetEnvValue(string key, string value) => FlareHome.SetEnvValueFrom(EnvFilePath, key, value);

    /// <summary>The `flare start` invocation that (re)initializes THIS instance specifically - reused by every command's "not initialized yet" message so the hint always names the right instance.</summary>
    public string StartHint => Name is null ? "flare start" : $"flare start -n {Name}";

    /// <summary>Short label for this instance in user-facing output - "default" for the unnamed instance, its own name otherwise.</summary>
    public string DisplayName => Name ?? "default";

    /// <summary>
    /// Deletes this instance's own config/credentials/state (`flare destroy --purge-config`)
    /// - but never anything it doesn't own. For a named instance that's simply removing
    /// its whole directory (exclusively this instance's, under instances/). The default
    /// instance is different: its <see cref="Directory"/> (RootDirectory) also hosts
    /// every NAMED instance's own directory (instances/), so deleting it wholesale would
    /// destroy every other instance too - only the default instance's own files
    /// (docker-compose.yml, .env, db/, state.json) are removed here, leaving instances/
    /// (and anything else under RootDirectory) untouched.
    /// </summary>
    public void PurgeConfig()
    {
        if (Name is not null)
        {
            System.IO.Directory.Delete(Directory, recursive: true);
            return;
        }

        if (File.Exists(ComposeFilePath))
        {
            File.Delete(ComposeFilePath);
        }

        if (File.Exists(EnvFilePath))
        {
            File.Delete(EnvFilePath);
        }

        if (File.Exists(StateFilePath))
        {
            File.Delete(StateFilePath);
        }

        if (System.IO.Directory.Exists(ClickHouseInitDirectory))
        {
            // ClickHouseInitDirectory is Directory/db/clickhouse - delete its "db" parent,
            // not just the clickhouse subfolder, to fully match what EnsureInitialized
            // creates and what the pre-multi-instance `flare destroy --purge-config`
            // removed for this same instance.
            System.IO.Directory.Delete(Path.Combine(Directory, "db"), recursive: true);
        }
    }
}

/// <summary>
/// Resolves and materializes Flare instances - the default one at <c>~/.flare/</c>, and
/// any number of named ones at <c>~/.flare/instances/&lt;name&gt;/</c>. The root itself is
/// resolved via <see cref="Environment.SpecialFolder.UserProfile"/> (not
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

    public static string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".flare");

    private static string InstancesDirectory => Path.Combine(RootDirectory, "instances");

    /// <summary>
    /// Resolves an instance by name. <c>null</c> (no `--name` passed - the overwhelmingly
    /// common case) resolves to the default instance at <see cref="RootDirectory"/>
    /// itself, byte-identical to this tool's behavior before multi-instance support
    /// existed. A non-null name resolves to <c>~/.flare/instances/&lt;name&gt;/</c> and must
    /// pass validation first.
    /// </summary>
    public static FlareInstance Resolve(string? instanceName)
    {
        if (instanceName is null)
        {
            return new FlareInstance(null, RootDirectory, "flare");
        }

        ValidateName(instanceName);
        return new FlareInstance(instanceName, Path.Combine(InstancesDirectory, instanceName), $"flare-{instanceName}");
    }

    /// <summary>
    /// Every named instance currently on disk, sorted by name - just directories under
    /// <c>instances/</c> that look initialized, no separate registry to drift out of sync
    /// with reality (a manually deleted instance directory just stops showing up here).
    /// Doesn't include the default instance - `flare instances list` combines this with
    /// <see cref="Resolve"/>(null) itself.
    /// </summary>
    public static IReadOnlyList<FlareInstance> ListNamedInstances()
    {
        if (!System.IO.Directory.Exists(InstancesDirectory))
        {
            return [];
        }

        return System.IO.Directory.EnumerateDirectories(InstancesDirectory)
            .Select(dir => Resolve(Path.GetFileName(dir)))
            .Where(instance => instance.IsInitialized)
            .OrderBy(instance => instance.Name, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Instance names are used as both a directory segment and a Docker Compose
    /// project-name suffix (<c>flare-&lt;name&gt;</c>) - restricted to lowercase
    /// alphanumerics/hyphens to stay safe in both contexts and legible in `docker ps`
    /// output. "default" is reserved (it's what omitting `--name` already means) so
    /// there's never two ways to say the same thing.
    /// </summary>
    private static void ValidateName(string instanceName)
    {
        var isValidShape = instanceName.Length > 0
            && instanceName.All(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-')
            && instanceName[0] != '-'
            && instanceName[^1] != '-';

        if (!isValidShape)
        {
            throw new ArgumentException(
                $"Instance name '{instanceName}' is invalid - use lowercase letters, digits, and hyphens only (not leading/trailing).",
                nameof(instanceName));
        }

        if (string.Equals(instanceName, "default", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Instance name 'default' is reserved - omit --name to target the default instance.",
                nameof(instanceName));
        }
    }

    /// <summary>
    /// Writes the compose file, a fresh randomly-keyed <c>.env</c>, and the ClickHouse
    /// init scripts from this assembly's embedded resources - but only if the instance
    /// isn't already initialized. Never overwrites an existing docker-compose.yml/.env in
    /// place: a re-run of `flare start` against an already-initialized instance must not
    /// clobber local edits or rotate credentials out from under an already-provisioned
    /// ClickHouse/identity store. For a named instance only, also auto-probes free host
    /// ports so its first-ever `.env` doesn't collide with the default (or another named)
    /// instance already running - the default instance always keeps the static,
    /// documented port defaults, so its own output here is unchanged from before
    /// multi-instance support existed.
    /// </summary>
    public static void EnsureInitialized(FlareInstance instance)
    {
        System.IO.Directory.CreateDirectory(instance.Directory);
        System.IO.Directory.CreateDirectory(instance.ClickHouseInitDirectory);

        var assembly = typeof(FlareHome).Assembly;

        if (!File.Exists(instance.ComposeFilePath))
        {
            File.WriteAllText(instance.ComposeFilePath, ReadEmbeddedResource(assembly, ComposeResourceName));
        }

        if (!File.Exists(instance.EnvFilePath))
        {
            var template = ReadEmbeddedResource(assembly, EnvTemplateResourceName);
            var ports = instance.Name is null ? null : PortDefaults.ProbeFreePorts();
            File.WriteAllText(instance.EnvFilePath, EnvGenerator.RenderEnvTemplate(template, ports));
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
                Path.Combine(instance.ClickHouseInitDirectory, fileName),
                ReadEmbeddedResource(assembly, resourceName));
        }

        if (!File.Exists(instance.StateFilePath))
        {
            StateMetadata.Save(instance.StateFilePath, new StateMetadata
            {
                ImageTag = "0.2.0",
                CliVersion = assembly.GetName().Version?.ToString() ?? "unknown",
            });
        }
    }

    /// <summary>
    /// Reads a single <c>KEY=value</c> line out of an instance's <c>.env</c>. Deliberately
    /// minimal (no quoting/escaping support) - every value this tool itself writes into
    /// the template is a plain token (port number, tag name, generated alphanumeric
    /// secret), and hand-edits beyond that are the user's own responsibility.
    /// </summary>
    internal static string ReadEnvValueFrom(string envFilePath, string key, string fallback)
    {
        if (!File.Exists(envFilePath))
        {
            return fallback;
        }

        foreach (var line in File.ReadLines(envFilePath))
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

    /// <summary>
    /// Rewrites a single <c>KEY=value</c> line in an instance's <c>.env</c> in place -
    /// everything else (comments, blank lines, other keys) is preserved verbatim, and a
    /// new line is appended at the end if the key isn't present yet. Same minimal
    /// KEY=value assumption as <see cref="ReadEnvValueFrom"/>: no quoting/escaping
    /// support, values written here are always a plain token (e.g. an image tag). This is
    /// the one piece of <c>.env</c> the CLI itself ever rewrites after first init - see
    /// <c>UpdateCommand</c>'s <c>--tag</c> option, the CLI-native alternative to hand-
    /// editing <c>.env</c> to move an existing install onto a newer image pin.
    /// </summary>
    internal static void SetEnvValueFrom(string envFilePath, string key, string value)
    {
        var lines = File.Exists(envFilePath) ? File.ReadAllLines(envFilePath).ToList() : [];
        var replaced = false;

        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = trimmed.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            if (!string.Equals(trimmed[..separatorIndex], key, StringComparison.Ordinal))
            {
                continue;
            }

            lines[i] = $"{key}={value}";
            replaced = true;
            break;
        }

        if (!replaced)
        {
            lines.Add($"{key}={value}");
        }

        File.WriteAllLines(envFilePath, lines);
    }

    private static string ReadEmbeddedResource(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' was expected but could not be opened.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
