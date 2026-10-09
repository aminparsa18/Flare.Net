using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Flare.Cli.Internal;

/// <summary>
/// One resource family <c>flare config export</c>/<c>apply</c> moves. <see cref="Section"/> is the
/// array's name in the config file; the paths are the REST endpoints it is read from and written to.
/// </summary>
internal sealed record ConfigKind(
    string Section,
    string Label,
    string ListPath,
    string ListProperty,
    IReadOnlyList<string> SecretFields)
{
    /// <summary>Root of the per-item endpoint (<c>PUT {ItemPath}/{id}</c>); the list path for every REST-shaped kind.</summary>
    public string ItemPath => ListPath;
}

internal enum ConfigOutcome
{
    Create,
    Update,
    Unchanged,
    Skip,
    Error,
}

internal sealed record ConfigPlanItem(string Section, string Name, ConfigOutcome Outcome, string? Detail = null, string? ExistingId = null);

/// <summary>An existing resource as read from the API: <see cref="Raw"/> is the (secret-masked) response, <see cref="Export"/> its portable form.</summary>
internal sealed record ConfigExisting(string Id, string Name, JsonObject Raw, JsonObject Export);

/// <summary>
/// Pure logic behind <c>flare config export</c>/<c>apply</c> - ids stripped on the way out, names
/// and <c>${ENV_VAR}</c> placeholders resolved on the way in, and the create/update/unchanged
/// planning. No I/O, so it is unit-tested directly (see <c>ConfigSyncTests</c>).
/// </summary>
internal static partial class ConfigSync
{
    public const int CurrentVersion = 1;

    public const string AlertsSection = "alerts";

    public const string IngestKeysSection = "ingestKeys";

    public const string WindowsSection = "maintenanceWindows";

    public const string ForwardingSection = "forwardingTargets";

    /// <summary>The archive is one settings object per instance, not a named list, so it has its own section shape.</summary>
    public const string ArchiveSection = "archive";

    private static readonly string[] ArchiveSecretFields = ["accessKey", "secretKey"];

    /// <summary>Apply order: later kinds reference earlier ones by name (alerts name channels and SLOs; windows name alerts).</summary>
    public static readonly IReadOnlyList<ConfigKind> RestKinds =
    [
        new("notificationChannels", "notification channel", "/api/notification-channels", "channels",
            ["webhookUrl", "telegramBotToken", "pagerDutyRoutingKey", "jiraApiToken", "incidentIoToken", "jsmOpsApiKey"]),
        new("slos", "SLO", "/api/slos", "slos", []),
        new("pipelineRules", "pipeline rule", "/api/pipeline-rules", "rules", []),
        new("metricAttributeRules", "metric attribute rule", "/api/metric-attribute-rules", "rules", []),
        new(ForwardingSection, "forwarding target", "/api/forwarding/targets", "targets", []),
    ];

    /// <summary>Planning-only: alert rules are read and written through <c>/api/alerts/export</c> and <c>/import</c>, not per-item REST.</summary>
    public static readonly ConfigKind Alerts = new(AlertsSection, "alert rule", "/api/alerts", "rules", []);

    public static readonly ConfigKind Windows = new(WindowsSection, "maintenance window", "/api/maintenance-windows", "windows", []);

    private static readonly string[] ServerFields = ["id", "createdAt", "updatedAt", "projectId"];

    private static readonly string[] KeyLimitFields = ["limitsEnabled", "maxEventsPerMinute", "maxBytesPerMinute", "maxEventsPerDay", "maxBytesPerDay"];

    [GeneratedRegex(@"\$\{([A-Za-z_][A-Za-z0-9_]*)\}")]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T")]
    private static partial Regex IsoDatePattern();

    /// <summary>The environment variable an exported secret placeholder names, e.g. <c>FLARE_NOTIFICATIONCHANNELS_SLACK_OPS_WEBHOOKURL</c>.</summary>
    public static string SecretVariable(string section, string itemName, string field)
    {
        static string Clean(string s) => Regex.Replace(s.ToUpperInvariant(), "[^A-Z0-9]+", "_").Trim('_');
        return $"FLARE_{Clean(section)}_{Clean(itemName)}_{Clean(field)}";
    }

    /// <summary>Placeholder variable for a field of a single-instance section such as the archive, e.g. <c>FLARE_ARCHIVE_ACCESSKEY</c>.</summary>
    public static string SecretVariable(string section, string field) =>
        $"FLARE_{Regex.Replace(section.ToUpperInvariant(), "[^A-Z0-9]+", "_").Trim('_')}_{Regex.Replace(field.ToUpperInvariant(), "[^A-Z0-9]+", "_").Trim('_')}";

    public static string NameOf(JsonObject item, string section) =>
        (section == AlertsSection ? item["rule"]?["name"]?.GetValue<string>() : item["name"]?.GetValue<string>()) ?? "";

    /// <summary>
    /// Portable form of an API response item: server-managed fields dropped, credentials replaced by
    /// <c>${ENV_VAR}</c> placeholders, and (windows) alert ids replaced by alert names.
    /// </summary>
    public static JsonObject ToExportItem(
        ConfigKind kind,
        JsonObject item,
        IReadOnlyDictionary<string, string> alertNamesById,
        IReadOnlyDictionary<string, string>? ingestKeyNamesById = null)
    {
        var result = (JsonObject)item.DeepClone();
        foreach (var field in ServerFields)
        {
            result.Remove(field);
        }

        var name = item["name"]?.GetValue<string>() ?? "";
        foreach (var field in kind.SecretFields)
        {
            if (result[field] is JsonValue v && v.TryGetValue<string>(out var value) && value.Length > 0)
            {
                result[field] = $"${{{SecretVariable(kind.Section, name, field)}}}";
            }
        }

        if (kind.Section == WindowsSection)
        {
            var ids = (result["ruleIds"] as JsonArray)?.Select(n => n?.GetValue<string>() ?? "").ToList() ?? [];
            result.Remove("ruleIds");
            if (ids.Count > 0)
            {
                result["ruleNames"] = new JsonArray([.. ids.Select(id => (JsonNode?)(alertNamesById.TryGetValue(id, out var n) ? n : id))]);
            }
        }

        if (kind.Section == ForwardingSection)
        {
            // Header values are credentials the API only returns masked: every one becomes a placeholder.
            if (result["headers"] is JsonObject headers)
            {
                var placeholders = new JsonObject();
                foreach (var (header, _) in headers)
                {
                    placeholders[header] = $"${{{SecretVariable(kind.Section, name, "headers." + header)}}}";
                }

                result["headers"] = placeholders;
            }

            var keyIds = (result["ingestKeyIds"] as JsonArray)?.Select(n => n?.GetValue<string>() ?? "").ToList() ?? [];
            result.Remove("ingestKeyIds");
            if (keyIds.Count > 0)
            {
                result["ingestKeyNames"] = new JsonArray([.. keyIds.Select(id => (JsonNode?)(ingestKeyNamesById is not null && ingestKeyNamesById.TryGetValue(id, out var n) ? n : id))]);
            }
        }

        return Prune(result) as JsonObject ?? new JsonObject();
    }

    /// <summary>Portable form of the archive settings: server fields dropped and both keys replaced by placeholders. Null when nothing is saved.</summary>
    public static JsonObject? ToExportArchive(JsonObject settings)
    {
        if (settings["saved"] is not JsonValue saved || !saved.TryGetValue<bool>(out var isSaved) || !isSaved)
        {
            return null;
        }

        var result = (JsonObject)settings.DeepClone();
        result.Remove("saved");
        result.Remove("updatedAt");
        foreach (var field in ArchiveSecretFields)
        {
            if (result[field] is JsonValue v && v.TryGetValue<string>(out var value) && value.Length > 0)
            {
                result[field] = $"${{{SecretVariable(ArchiveSection, field)}}}";
            }
        }

        return Prune(result) as JsonObject;
    }

    /// <summary>
    /// Request body for <c>PUT /api/archive/settings</c>. As for other credentials, an unset variable
    /// keeps the stored key on an update (the masked value is sent back) and is an error on a first save.
    /// </summary>
    public static (JsonObject? Body, bool SecretsSent, string? Error) BuildArchiveBody(JsonObject desired, JsonObject? existing, Func<string, string?> env)
    {
        var body = (JsonObject)desired.DeepClone();
        var (sent, error) = ResolveSecrets(body, ArchiveSecretFields, field => existing?[field], env);
        if (error is not null)
        {
            return (null, false, error);
        }

        var unset = new List<string>();
        body = (JsonObject)Expand(body, env, unset)!;
        return unset.Count > 0 ? (null, false, $"Environment variable(s) not set: {string.Join(", ", unset.Distinct())}.") : (body, sent, null);
    }

    /// <summary>Whether the archive in the file differs from what is saved; keys can't be compared, so supplying one always counts.</summary>
    public static ConfigOutcome PlanArchive(JsonObject desired, JsonObject? existingExport, Func<string, string?> env)
    {
        if (existingExport is null)
        {
            return ConfigOutcome.Create;
        }

        return SuppliesSecret(desired, ArchiveSecretFields, env) || Canonical(WithoutSecrets(desired, ArchiveSecretFields)) != Canonical(WithoutSecrets(existingExport, ArchiveSecretFields))
            ? ConfigOutcome.Update
            : ConfigOutcome.Unchanged;
    }

    /// <summary>True when any of <paramref name="fields"/> carries a literal value or a placeholder the environment can fill.</summary>
    private static bool SuppliesSecret(JsonObject item, IEnumerable<string> fields, Func<string, string?> env) =>
        fields.Any(field =>
        {
            if (item[field] is not JsonValue v || !v.TryGetValue<string>(out var value) || value.Length == 0)
            {
                return false;
            }

            var whole = PlaceholderPattern().Match(value);
            return !whole.Success || whole.Length != value.Length || env(whole.Groups[1].Value) is not null;
        });

    private static JsonObject WithoutSecrets(JsonObject item, string[] fields)
    {
        var copy = (JsonObject)item.DeepClone();
        foreach (var field in fields)
        {
            copy.Remove(field);
        }

        return copy;
    }

    /// <summary>
    /// Resolves each whole-value <c>${VAR}</c> in <paramref name="fields"/> of <paramref name="body"/>: from the
    /// environment, else the stored (masked) value, else an error. Returns whether any real secret was sent.
    /// </summary>
    private static (bool SecretsSent, string? Error) ResolveSecrets(JsonObject body, IEnumerable<string> fields, Func<string, JsonNode?> stored, Func<string, string?> env)
    {
        var secretsSent = false;
        foreach (var field in fields)
        {
            if (body[field] is not JsonValue v || !v.TryGetValue<string>(out var value))
            {
                continue;
            }

            var whole = PlaceholderPattern().Match(value);
            if (whole.Success && whole.Length == value.Length)
            {
                var resolved = env(whole.Groups[1].Value);
                if (resolved is not null)
                {
                    body[field] = resolved;
                    secretsSent = true;
                }
                else if (stored(field) is JsonValue prior && prior.TryGetValue<string>(out var masked) && masked.Length > 0)
                {
                    body[field] = masked;
                }
                else
                {
                    return (false, $"{field}: environment variable {whole.Groups[1].Value} is not set.");
                }
            }
            else
            {
                secretsSent = true;
            }
        }

        return (secretsSent, null);
    }

    /// <summary>An ingest key's versionable part: its name and rate limits (the key itself is a credential and is never exported).</summary>
    public static JsonObject? ToExportKey(JsonObject key)
    {
        if (key["isActive"] is JsonValue active && active.TryGetValue<bool>(out var isActive) && !isActive)
        {
            return null;
        }

        var result = new JsonObject { ["name"] = key["name"]?.DeepClone() };
        foreach (var field in KeyLimitFields)
        {
            if (key[field] is { } value)
            {
                result[field] = value.DeepClone();
            }
        }

        return Prune(result) as JsonObject;
    }

    /// <summary>
    /// The request body to POST/PUT for <paramref name="desired"/>: <c>${VAR}</c> expanded from
    /// <paramref name="env"/>, window <c>ruleNames</c> turned into <c>ruleIds</c>. A credential whose
    /// variable is unset keeps the stored secret on an update (the API treats its own mask as
    /// "unchanged") and is an error on a create.
    /// </summary>
    public static (JsonObject? Body, bool SecretsSent, string? Error) BuildBody(
        ConfigKind kind,
        JsonObject desired,
        ConfigExisting? existing,
        Func<string, string?> env,
        IReadOnlyDictionary<string, string> alertIdsByName,
        bool allowPendingRules,
        IReadOnlyDictionary<string, string>? ingestKeyIdsByName = null)
    {
        var body = (JsonObject)desired.DeepClone();
        foreach (var field in ServerFields)
        {
            body.Remove(field);
        }

        var (secretsSent, secretError) = ResolveSecrets(body, kind.SecretFields, field => existing?.Raw[field], env);
        if (secretError is not null)
        {
            return (null, false, secretError);
        }

        if (kind.Section == ForwardingSection && body["headers"] is JsonObject headers)
        {
            var headerSecrets = ResolveSecrets(headers, headers.Select(h => h.Key).ToList(), header => (existing?.Raw["headers"] as JsonObject)?[header], env);
            if (headerSecrets.Error is not null)
            {
                return (null, false, "headers." + headerSecrets.Error);
            }

            secretsSent |= headerSecrets.SecretsSent;
        }

        var unset = new List<string>();
        body = (JsonObject)Expand(body, env, unset)!;
        if (unset.Count > 0)
        {
            return (null, false, $"Environment variable(s) not set: {string.Join(", ", unset.Distinct())}.");
        }

        if (kind.Section == WindowsSection && body["ruleNames"] is JsonArray names)
        {
            var ids = new JsonArray();
            var missing = new List<string>();
            foreach (var n in names)
            {
                var ruleName = n?.GetValue<string>() ?? "";
                if (alertIdsByName.TryGetValue(ruleName, out var id))
                {
                    ids.Add(id);
                }
                else
                {
                    missing.Add(ruleName);
                }
            }

            if (missing.Count > 0 && !allowPendingRules)
            {
                return (null, false, $"Unknown alert rule(s): {string.Join(", ", missing)}.");
            }

            body.Remove("ruleNames");
            body["ruleIds"] = ids;
        }

        if (kind.Section == ForwardingSection && body["ingestKeyNames"] is JsonArray keyNames)
        {
            var ids = new JsonArray();
            var missing = new List<string>();
            foreach (var n in keyNames)
            {
                var keyName = n?.GetValue<string>() ?? "";
                if (ingestKeyIdsByName is not null && ingestKeyIdsByName.TryGetValue(keyName, out var id))
                {
                    ids.Add(id);
                }
                else
                {
                    missing.Add(keyName);
                }
            }

            if (missing.Count > 0 && !allowPendingRules)
            {
                return (null, false, $"Unknown active ingest key(s): {string.Join(", ", missing)}.");
            }

            body.Remove("ingestKeyNames");
            body["ingestKeyIds"] = ids;
        }

        return (body, secretsSent, null);
    }

    /// <summary>
    /// Decides create/update/unchanged for each desired item of one kind against what exists. A
    /// credential that resolves from the environment can't be compared with its masked stored value,
    /// so such an item always counts as an update.
    /// </summary>
    public static List<ConfigPlanItem> Plan(
        ConfigKind kind,
        IReadOnlyList<JsonObject> desired,
        IReadOnlyList<ConfigExisting> existing,
        Func<string, string?> env)
    {
        var byName = ByName(existing);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var plan = new List<ConfigPlanItem>();
        foreach (var item in desired)
        {
            var name = NameOf(item, kind.Section);
            if (name.Length == 0)
            {
                plan.Add(new ConfigPlanItem(kind.Section, "(unnamed)", ConfigOutcome.Error, "Entry has no name."));
                continue;
            }

            if (!seen.Add(name))
            {
                plan.Add(new ConfigPlanItem(kind.Section, name, ConfigOutcome.Error, "Name appears more than once in the file."));
                continue;
            }

            if (!byName.TryGetValue(name, out var current))
            {
                plan.Add(new ConfigPlanItem(kind.Section, name, ConfigOutcome.Create));
                continue;
            }

            var (_, secretsSent, _) = BuildBody(kind, item, current, env, new Dictionary<string, string>(), allowPendingRules: true);
            var differs = Canonical(Comparable(kind, item)) != Canonical(Comparable(kind, current.Export));
            plan.Add(differs || secretsSent
                ? new ConfigPlanItem(kind.Section, name, ConfigOutcome.Update, secretsSent && !differs ? "credentials supplied from the environment" : null, current.Id)
                : new ConfigPlanItem(kind.Section, name, ConfigOutcome.Unchanged, ExistingId: current.Id));
        }

        return plan;
    }

    /// <summary>Same as <see cref="Plan"/> for ingest keys, which can only have their limits updated - never be created from a file.</summary>
    public static List<ConfigPlanItem> PlanKeys(IReadOnlyList<JsonObject> desired, IReadOnlyList<ConfigExisting> existing)
    {
        var byName = ByName(existing);
        var plan = new List<ConfigPlanItem>();
        foreach (var item in desired)
        {
            var name = NameOf(item, IngestKeysSection);
            if (!byName.TryGetValue(name, out var current))
            {
                plan.Add(new ConfigPlanItem(IngestKeysSection, name, ConfigOutcome.Skip, "No active key with this name - create it with `flare apikey create` (keys are credentials, never created from a file)."));
                continue;
            }

            plan.Add(Canonical(item) == Canonical(current.Export)
                ? new ConfigPlanItem(IngestKeysSection, name, ConfigOutcome.Unchanged, ExistingId: current.Id)
                : new ConfigPlanItem(IngestKeysSection, name, ConfigOutcome.Update, ExistingId: current.Id));
        }

        return plan;
    }

    /// <summary>The body for <c>PUT /api/ingest-keys/{id}/limits</c>; a key with no limits fields turns limits off.</summary>
    public static JsonObject KeyLimitsBody(JsonObject desired)
    {
        var body = new JsonObject { ["limitsEnabled"] = desired["limitsEnabled"]?.GetValue<bool>() ?? false };
        foreach (var field in KeyLimitFields.Skip(1))
        {
            if (desired[field] is { } value)
            {
                body[field] = value.DeepClone();
            }
        }

        return body;
    }

    public static Dictionary<string, ConfigExisting> ByName(IEnumerable<ConfigExisting> existing)
    {
        var map = new Dictionary<string, ConfigExisting>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in existing)
        {
            map.TryAdd(e.Name, e);
        }

        return map;
    }

    /// <summary>The part of an item worth comparing: no credentials (the API only returns masks) and window rule names in a stable order.</summary>
    private static JsonObject Comparable(ConfigKind kind, JsonObject item)
    {
        var copy = (JsonObject)item.DeepClone();
        foreach (var field in kind.SecretFields)
        {
            copy.Remove(field);
        }

        if (copy["headers"] is JsonObject headers)
        {
            // Values are masked or placeholders on one side; only which headers exist is comparable.
            copy["headerNames"] = new JsonArray([.. headers.Select(h => h.Key).Order(StringComparer.OrdinalIgnoreCase).Select(n => (JsonNode?)n)]);
            copy.Remove("headers");
        }

        foreach (var field in new[] { "signals", "services", "ingestKeyNames" })
        {
            if (copy[field] is JsonArray list)
            {
                copy[field] = new JsonArray([.. list.Select(n => n?.GetValue<string>() ?? "").Order(StringComparer.OrdinalIgnoreCase).Select(n => (JsonNode?)n)]);
            }
        }

        if (copy["ruleNames"] is JsonArray names)
        {
            copy["ruleNames"] = new JsonArray([.. names.Select(n => n?.GetValue<string>() ?? "").Order(StringComparer.OrdinalIgnoreCase).Select(n => (JsonNode?)n)]);
        }

        return copy;
    }

    /// <summary>Expands <c>${VAR}</c> in every string; names with no value are added to <paramref name="unset"/>.</summary>
    private static JsonNode? Expand(JsonNode? node, Func<string, string?> env, List<string> unset)
    {
        switch (node)
        {
            case JsonObject o:
                var result = new JsonObject();
                foreach (var (key, value) in o)
                {
                    result[key] = Expand(value, env, unset);
                }

                return result;
            case JsonArray a:
                return new JsonArray([.. a.Select(n => Expand(n, env, unset))]);
            case JsonValue v when v.TryGetValue<string>(out var s):
                return JsonValue.Create(PlaceholderPattern().Replace(s, m =>
                {
                    var value = env(m.Groups[1].Value);
                    if (value is null)
                    {
                        unset.Add(m.Groups[1].Value);
                        return m.Value;
                    }

                    return value;
                }));
            default:
                return node?.DeepClone();
        }
    }

    /// <summary>
    /// Order-insensitive-for-objects, format-insensitive string for comparing two representations
    /// of the same resource: nulls/empty strings/empty collections are dropped, numbers compared by
    /// value and ISO timestamps by instant, so a hand-written file and an API response line up.
    /// </summary>
    public static string Canonical(JsonNode? node)
    {
        var sb = new StringBuilder();
        Write(Prune(node), sb);
        return sb.ToString();
    }

    private static JsonNode? Prune(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o:
                var obj = new JsonObject();
                foreach (var (key, value) in o)
                {
                    if (Prune(value) is { } pruned)
                    {
                        obj[key] = pruned;
                    }
                }

                return obj.Count == 0 ? null : obj;
            case JsonArray a:
                var arr = new JsonArray([.. a.Select(Prune).Where(n => n is not null)]);
                return arr.Count == 0 ? null : arr;
            case JsonValue v:
                if (v.TryGetValue<string>(out var s))
                {
                    if (s.Length == 0)
                    {
                        return null;
                    }

                    return IsoDatePattern().IsMatch(s) && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var instant)
                        ? JsonValue.Create(instant.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))
                        : JsonValue.Create(s);
                }

                return v.GetValueKind() == System.Text.Json.JsonValueKind.Number ? JsonValue.Create(v.GetValue<double>()) : v.DeepClone();
            default:
                return null;
        }
    }

    private static void Write(JsonNode? node, StringBuilder sb)
    {
        switch (node)
        {
            case JsonObject o:
                sb.Append('{');
                foreach (var (key, value) in o.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    sb.Append(key).Append(':');
                    Write(value, sb);
                    sb.Append(',');
                }

                sb.Append('}');
                break;
            case JsonArray a:
                sb.Append('[');
                foreach (var n in a)
                {
                    Write(n, sb);
                    sb.Append(',');
                }

                sb.Append(']');
                break;
            default:
                sb.Append(node?.ToJsonString() ?? "null");
                break;
        }
    }
}
