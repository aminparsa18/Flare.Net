using System.Text.Json;
using System.Text.Json.Nodes;

namespace Flare.Api.Auditing;

/// <summary>One changed field in an audit event; secret values are replaced by <see cref="AuditDiff.Redacted"/>.</summary>
public sealed record AuditFieldChange(string Field, string? Before, string? After);

/// <summary>
/// Field-level diff of two JSON snapshots of a resource, with secrets redacted (ADR-0081).
/// Pure so it is unit-testable without hosting. Nested objects are flattened to dotted
/// paths (<c>condition.minLevel</c>); arrays and scalars compare as whole values.
/// </summary>
public static class AuditDiff
{
    public const string Redacted = "[redacted]";

    internal const int MaxChanges = 50;
    internal const int MaxValueLength = 300;

    // A field is redacted when its last path segment contains any of these (case-insensitive).
    // Deliberately broad: a false positive hides a harmless value, a false negative leaks a secret.
    private static readonly string[] SecretMarkers =
        ["secret", "password", "token", "url", "key", "authorization", "credential", "webhook", "bearer", "dsn"];

    public static bool IsSecretField(string path)
    {
        var name = path[(path.LastIndexOf('.') + 1)..];
        foreach (var marker in SecretMarkers)
        {
            if (name.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The changed fields, ordered by path; empty when the snapshots are equal.</summary>
    public static IReadOnlyList<AuditFieldChange> Compute(JsonNode? before, JsonNode? after)
    {
        var left = new SortedDictionary<string, JsonNode?>(StringComparer.Ordinal);
        var right = new SortedDictionary<string, JsonNode?>(StringComparer.Ordinal);
        Flatten(before, string.Empty, left);
        Flatten(after, string.Empty, right);

        var changes = new List<AuditFieldChange>();
        foreach (var path in left.Keys.Union(right.Keys).Order(StringComparer.Ordinal))
        {
            left.TryGetValue(path, out var l);
            right.TryGetValue(path, out var r);
            var lText = Render(l);
            var rText = Render(r);
            if (lText == rText || IsBookkeeping(path))
            {
                continue;
            }

            if (changes.Count == MaxChanges)
            {
                break;
            }

            changes.Add(IsSecretField(path)
                ? new AuditFieldChange(path, lText is null ? null : Redacted, rText is null ? null : Redacted)
                : new AuditFieldChange(path, Truncate(lText), Truncate(rText)));
        }

        return changes;
    }

    /// <summary>Serialized <see cref="Compute"/> result for the <c>Changes</c> column, or null when nothing changed.</summary>
    public static string? ComputeJson(JsonNode? before, JsonNode? after)
    {
        var changes = Compute(before, after);
        return changes.Count == 0 ? null : JsonSerializer.Serialize(changes, AuditChangesJson.Options);
    }

    // Bumped by every save, so it would be a "change" on every update.
    private static bool IsBookkeeping(string path) =>
        path.Equals("updatedAt", StringComparison.OrdinalIgnoreCase);

    private static void Flatten(JsonNode? node, string path, IDictionary<string, JsonNode?> into)
    {
        if (node is JsonObject obj)
        {
            foreach (var (name, child) in obj)
            {
                Flatten(child, path.Length == 0 ? name : $"{path}.{name}", into);
            }
        }
        else if (path.Length > 0)
        {
            into[path] = node;
        }
    }

    // Null (JSON null / absent) renders as null so "field added" and "field removed" show
    // one empty side; strings render unquoted, everything else as compact JSON.
    private static string? Render(JsonNode? node) => node switch
    {
        null => null,
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        _ => node.ToJsonString(),
    };

    private static string? Truncate(string? value) =>
        value is { Length: > MaxValueLength } ? value[..MaxValueLength] + "…" : value;
}

/// <summary>Camel-case reflection options for the small <see cref="AuditFieldChange"/> list; shared by writer and reader.</summary>
internal static class AuditChangesJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
