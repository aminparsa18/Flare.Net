namespace Flare.Api.Auditing;

/// <summary>
/// Lets a handler tell the audit middleware the id of the resource it just created - a create
/// has no id in its route, only in the handler's own result.
/// </summary>
public static class AuditContext
{
    private const string ResourceIdKey = "flare:audit:resource-id";

    public static void SetResourceId(HttpContext http, object id) => http.Items[ResourceIdKey] = id.ToString();

    internal static string? GetResourceId(HttpContext http) => http.Items.TryGetValue(ResourceIdKey, out var v) ? v as string : null;
}
