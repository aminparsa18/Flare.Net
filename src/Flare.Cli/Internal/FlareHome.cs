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

    /// <summary>Cluster-topology-only: where keeper/macros/remote-servers/nginx-lb config gets materialized, matching the relative path <c>Templates/docker-compose.cluster.flare.yml</c>'s own volume mounts expect.</summary>
    public string ClusterConfigDirectory => Path.Combine(Directory, "db", "clickhouse-cluster", "config");

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

        var dbDirectory = Path.Combine(Directory, "db");
        if (System.IO.Directory.Exists(dbDirectory))
        {
            // ClickHouseInitDirectory/ClusterConfigDirectory are both Directory/db/... -
            // delete the shared "db" parent wholesale rather than checking either
            // topology's own subfolder specifically, to fully match what
            // EnsureInitialized creates (Standalone: db/clickhouse/, Cluster:
            // db/clickhouse-cluster/config/) and what the pre-multi-instance
            // `flare destroy --purge-config` removed for this same instance.
            System.IO.Directory.Delete(dbDirectory, recursive: true);
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
    private const string ClickHouseInitResourcePrefix = "Flare.Cli.Templates.ClickHouseInit.";
    private const string ClusterConfigResourcePrefix = "Flare.Cli.Templates.ClusterConfig.";

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

    /// <summary>Opt-in env var shorthand for `--name`, checked when no `--name` was passed - see <see cref="ResolveTarget"/>.</summary>
    private const string InstanceEnvVarName = "FLARE_INSTANCE";

    /// <summary>
    /// The instance a command should actually operate on, given its own `--name` (or
    /// lack of one) - every command's entry point calls this instead of
    /// <see cref="Resolve"/> directly. Identical to <see cref="Resolve"/> except when NO
    /// name was passed:
    /// <list type="number">
    /// <item>an explicit `--name` always wins, full stop - never overridden by the env
    /// var or the auto-detect below;</item>
    /// <item>otherwise, a non-empty <c>FLARE_INSTANCE</c> env var is used as if it had
    /// been passed as `--name` - the opt-in shorthand from the docs' "Known
    /// limitations" section, for a shell/CI context that wants every invocation to
    /// target one named instance without repeating `-n` on each one.
    /// <c>FLARE_INSTANCE=default</c> explicitly targets the default instance (bypasses
    /// the auto-detect below entirely), mirroring how omitting `--name` behaves once
    /// the default instance exists;</item>
    /// <item>otherwise (no `--name`, no env var), if the default instance isn't
    /// initialized and exactly one named instance exists, that one is targeted instead
    /// of forcing `--name`/`FLARE_INSTANCE` on every command when there's only ever
    /// been one instance on the machine to begin with. Two or more named instances
    /// (still no default) stays ambiguous - resolves to the (uninitialized) default,
    /// the same "not initialized, run flare start" outcome as before, rather than
    /// guessing which one was meant. Never fires once the default instance exists -
    /// that keeps being everyone's implicit target, unambiguously, forever, regardless
    /// of how many named instances also exist alongside it.</item>
    /// </list>
    /// </summary>
    public static FlareInstance ResolveTarget(string? instanceName)
    {
        if (instanceName is not null)
        {
            return Resolve(instanceName);
        }

        var envInstanceName = Environment.GetEnvironmentVariable(InstanceEnvVarName);
        if (!string.IsNullOrEmpty(envInstanceName))
        {
            return Resolve(string.Equals(envInstanceName, "default", StringComparison.Ordinal) ? null : envInstanceName);
        }

        var defaultInstance = Resolve(null);
        if (defaultInstance.IsInitialized)
        {
            return defaultInstance;
        }

        var namedInstances = ListNamedInstances();
        return namedInstances.Count == 1 ? namedInstances[0] : defaultInstance;
    }

    /// <summary>
    /// The topology an already-initialized instance was created with - read from its
    /// own <c>state.json</c> (see <see cref="StateMetadata.Topology"/>), defaulting to
    /// <see cref="FlareTopology.Standalone"/> for an uninitialized instance or one whose
    /// <c>state.json</c> predates cluster mode. Every command except <c>flare start</c>
    /// (which decides the topology of a brand-new instance via its own <c>--cluster</c>
    /// flag, before <c>state.json</c> exists) should call this rather than assume
    /// Standalone.
    /// </summary>
    public static TopologyProfile ResolveTopology(FlareInstance instance)
    {
        if (!File.Exists(instance.StateFilePath))
        {
            return TopologyProfile.For(FlareTopology.Standalone);
        }

        var state = StateMetadata.Load(instance.StateFilePath);
        return TopologyProfile.Parse(state.Topology);
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
    /// init scripts (Standalone) or cluster config (Cluster) from this assembly's
    /// embedded resources - but only if the instance isn't already initialized. Never
    /// overwrites an existing docker-compose.yml/.env in place: a re-run of `flare start`
    /// against an already-initialized instance must not clobber local edits or rotate
    /// credentials out from under an already-provisioned ClickHouse/identity store. For a
    /// named instance only, also auto-probes free host ports so its first-ever <c>.env</c>
    /// doesn't collide with the default (or another named) instance already running - the
    /// default instance always keeps the static, documented port defaults, so its own
    /// output here is unchanged from before multi-instance support existed.
    ///
    /// <paramref name="topology"/> is only consulted for a brand-new instance (nothing on
    /// disk yet) - an already-initialized instance keeps whatever topology it was first
    /// created with regardless of what's passed here, EXCEPT that a mismatch throws
    /// rather than silently ignoring the caller's request: this isn't a live migration
    /// path (same posture docs/clustering.md already states for the repo-level cluster
    /// compose file), so `flare start --cluster` against an already-initialized
    /// Standalone instance (or vice versa) is a caller error to surface, not something to
    /// paper over.
    /// </summary>
    public static void EnsureInitialized(FlareInstance instance, FlareTopology topology)
    {
        if (instance.IsInitialized)
        {
            var existing = ResolveTopology(instance);
            if (existing.Topology != topology)
            {
                throw new InvalidOperationException(
                    $"Instance '{instance.DisplayName}' was already initialized as {existing.DisplayLabel} - "
                    + "not a live migration path. Use a different --name, or `flare destroy --purge-config` first.");
            }
        }

        var profile = TopologyProfile.For(topology);
        var assembly = typeof(FlareHome).Assembly;

        System.IO.Directory.CreateDirectory(instance.Directory);

        if (!File.Exists(instance.ComposeFilePath))
        {
            File.WriteAllText(instance.ComposeFilePath, ReadEmbeddedResource(assembly, profile.ComposeResourceName));
        }

        if (!File.Exists(instance.EnvFilePath))
        {
            var template = ReadEmbeddedResource(assembly, profile.EnvTemplateResourceName);
            var ports = instance.Name is null ? null : PortDefaults.ProbeFreePorts(profile.Ports);
            File.WriteAllText(instance.EnvFilePath, EnvGenerator.RenderEnvTemplate(template, profile.Ports, ports));
        }

        // Cheap and idempotent to re-extract every run (unlike the compose/env files,
        // these are never hand-edited and always need to match this CLI version's
        // schema exactly) - so no "only if missing" guard here. Standalone extracts
        // ClickHouse init SQL (bind-mounted into the clickhouse container); Cluster
        // extracts keeper/macros/remote-servers/nginx-lb config instead (bind-mounted
        // into the keeper/clickhouse/clickhouse-lb containers) - cluster mode's own
        // schema is applied by ClickHouseMigrationRunner at app startup instead, already
        // embedded inside the published ingest/api images, so there's no SQL to extract
        // here for it (see docs/clustering.md).
        if (topology == FlareTopology.Standalone)
        {
            System.IO.Directory.CreateDirectory(instance.ClickHouseInitDirectory);
            ExtractResources(assembly, ClickHouseInitResourcePrefix, instance.ClickHouseInitDirectory);
        }
        else
        {
            System.IO.Directory.CreateDirectory(instance.ClusterConfigDirectory);
            ExtractResources(assembly, ClusterConfigResourcePrefix, instance.ClusterConfigDirectory);
        }

        if (!File.Exists(instance.StateFilePath))
        {
            StateMetadata.Save(instance.StateFilePath, new StateMetadata
            {
                ImageTag = profile.DefaultImageTag,
                CliVersion = assembly.GetName().Version?.ToString() ?? "unknown",
                Topology = profile.DisplayLabel,
            });
        }
    }

    private static void ExtractResources(Assembly assembly, string resourcePrefix, string targetDirectory)
    {
        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(resourcePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var fileName = resourceName[resourcePrefix.Length..];
            File.WriteAllText(Path.Combine(targetDirectory, fileName), ReadEmbeddedResource(assembly, resourceName));
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
