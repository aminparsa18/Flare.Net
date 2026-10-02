using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Flare.Api.Auditing;

/// <summary>
/// Lets a handler tell the audit middleware the id of the resource it just created - a create
/// has no id in its route, only in the handler's own result.
/// </summary>
public static class AuditContext
{
    private const string ChangesKey = "flare:audit:changes";
    private const string ResourceIdKey = "flare:audit:resource-id";

    public static void SetResourceId(HttpContext http, object id) => http.Items[ResourceIdKey] = id.ToString();

    /// <summary>
    /// Reports what an update changed: the resource as it was and as it is now, serialized with
    /// the handler's own source-generated type info. The diff and secret redaction happen here,
    /// so neither snapshot is ever stored. Either side may be null (create/delete).
    /// </summary>
    public static void SetChange<T>(HttpContext http, JsonTypeInfo<T> typeInfo, T? before, T? after)
        where T : class
    {
        var changes = AuditDiff.ComputeJson(
            before is null ? null : JsonSerializer.SerializeToNode(before, typeInfo),
            after is null ? null : JsonSerializer.SerializeToNode(after, typeInfo));
        if (changes is not null)
        {
            http.Items[ChangesKey] = changes;
        }
    }

    /// <summary>
    /// Reflection-serialized variant for the singleton settings records, which have no
    /// source-generated type info and (unlike the response DTOs) carry the real secret, so a
    /// rotated secret still shows up as a redacted change.
    /// </summary>
    public static void SetChange<T>(HttpContext http, T? before, T? after)
        where T : class
    {
        var changes = AuditDiff.ComputeJson(
            before is null ? null : JsonSerializer.SerializeToNode(before, AuditChangesJson.Options),
            after is null ? null : JsonSerializer.SerializeToNode(after, AuditChangesJson.Options));
        if (changes is not null)
        {
            http.Items[ChangesKey] = changes;
        }
    }

    internal static string? GetChanges(HttpContext http) => http.Items.TryGetValue(ChangesKey, out var v) ? v as string : null;

    internal static string? GetResourceId(HttpContext http) => http.Items.TryGetValue(ResourceIdKey, out var v) ? v as string : null;
}
