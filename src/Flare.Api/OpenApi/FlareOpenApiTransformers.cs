using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Flare.Api.OpenApi;

/// <summary>
/// Shapes the generated OpenAPI document: declares the two credentials Flare.Api accepts and
/// groups operations under hierarchical tags (a parent area such as "Telemetry" with children
/// such as "logs"). Parent/kind/summary are OpenAPI 3.2 tag fields; they are only written when the
/// document is emitted as 3.2 (see <c>OpenApi:EmitV32</c>), older versions just get the flat tags.
/// </summary>
public static class FlareOpenApiTransformers
{
    public const string CookieScheme = "FlareSession";
    public const string BearerScheme = "PersonalAccessToken";

    // /api/{segment}/... -> parent area. Anything unlisted falls under "Administration".
    private static readonly Dictionary<string, string> Parents = new(StringComparer.OrdinalIgnoreCase)
    {
        ["logs"] = "Telemetry", ["traces"] = "Telemetry", ["spans"] = "Telemetry", ["metrics"] = "Telemetry",
        ["services"] = "Telemetry", ["errors"] = "Telemetry", ["profiles"] = "Telemetry", ["hosts"] = "Telemetry",
        ["kubernetes"] = "Telemetry", ["messaging"] = "Telemetry", ["external-apis"] = "Telemetry",
        ["app-sessions"] = "Telemetry", ["web-replay"] = "Telemetry", ["releases"] = "Telemetry",
        ["alerts"] = "Alerting", ["notification-channels"] = "Alerting", ["alert-templates"] = "Alerting",
        ["maintenance-windows"] = "Alerting", ["oncall-rotations"] = "Alerting", ["slos"] = "Alerting",
        ["checkin-monitors"] = "Alerting", ["synthetic-monitors"] = "Alerting",
        ["dashboards"] = "Dashboards", ["dashboard-schedules"] = "Dashboards", ["views"] = "Dashboards",
        ["status-pages"] = "Dashboards",
    };

    private const string DefaultParent = "Administration";

    /// <summary>Declares the session cookie and the <c>flr_pat_…</c> Bearer token as security schemes.</summary>
    public static IOpenApiDocumentTransformer SecuritySchemes(string cookieName) =>
        new DelegateDocumentTransformer((document, _, _) =>
        {
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes[CookieScheme] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Cookie,
                Name = cookieName,
                Description = "Session cookie set by the dashboard login (local, Entra ID, LDAP, OIDC or trusted-header sign-in).",
            };
            document.Components.SecuritySchemes[BearerScheme] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "flr_pat_…",
                Description = "Personal access token created under Settings → Access tokens, sent as 'Authorization: Bearer flr_pat_…'.",
            };
            document.Security =
            [
                new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(CookieScheme, document)] = [] },
                new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(BearerScheme, document)] = [] },
            ];
            return Task.CompletedTask;
        });

    /// <summary>Tags each operation by its <c>/api/{segment}</c> and declares the tag hierarchy.</summary>
    public static IOpenApiDocumentTransformer Tags() =>
        new DelegateDocumentTransformer((document, _, _) =>
        {
            var used = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (path, item) in document.Paths)
            {
                var segment = SegmentOf(path);
                if (segment is null || item.Operations is null)
                {
                    continue;
                }

                used.Add(segment);
                foreach (var operation in item.Operations.Values)
                {
                    operation.Tags = new HashSet<OpenApiTagReference> { new(segment, document) };
                }
            }

            var tags = new List<OpenApiTag>();
            foreach (var parent in used.Select(ParentOf).Distinct().Order())
            {
                tags.Add(new OpenApiTag { Name = parent, Kind = "nav", Summary = parent });
            }

            foreach (var segment in used)
            {
                tags.Add(new OpenApiTag
                {
                    Name = segment,
                    Summary = segment,
                    Kind = "nav",
                    Parent = new OpenApiTagReference(ParentOf(segment), document),
                });
            }

            document.Tags = new HashSet<OpenApiTag>(tags);
            return Task.CompletedTask;
        });

    internal static string? SegmentOf(string path)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && parts[0] == "api" ? parts[1] : null;
    }

    internal static string ParentOf(string segment) =>
        Parents.TryGetValue(segment, out var parent) ? parent : DefaultParent;

    private sealed class DelegateDocumentTransformer(
        Func<OpenApiDocument, OpenApiDocumentTransformerContext, CancellationToken, Task> run) : IOpenApiDocumentTransformer
    {
        public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken) =>
            run(document, context, cancellationToken);
    }
}
