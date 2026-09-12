using MemoryPack;
using System.Text.Json;

namespace Flare.Api.Model;

/// <summary>
/// A named, multi-panel dashboard composed from arbitrary log/trace/metric queries - the
/// "Custom, user-built dashboards" roadmap item, and the sibling <see cref="SavedView"/>
/// was explicitly scoped to not cover (see that type's remarks). See
/// <c>docs-internal/adr/0023-custom-dashboards.md</c> for the full design rationale.
/// </summary>
/// <remarks>
/// <see cref="LayoutJson"/> is kept as an opaque <see cref="JsonElement"/> deliberately -
/// same reasoning <see cref="SavedView.State"/>'s remarks give for that column. It holds a
/// dashboard-TypeScript-owned array of panels (<c>{ id, panelType, title, layout: {x,y,w,h},
/// query }</c>), where each panel's <c>query</c> is exactly the same
/// <c>LogsFilterState</c>/<c>TracesFilterState</c>/<c>MetricsFilterState</c> shape a
/// <see cref="SavedView"/> of that page type already stores. <see cref="Query.DashboardQueryService"/>
/// only ever round-trips this column through ClickHouse's opaque <c>LayoutJson</c> column -
/// it's never deserialized into a Flare.Api type, and no code here inspects its contents or
/// executes a panel's query server-side (panel execution is entirely client-driven, against
/// the same <c>/api/logs/*</c>/<c>/api/metrics/*</c>/traces endpoints the Explorer pages
/// already call).
/// </remarks>
[MemoryPackable]
public sealed partial record Dashboard
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = "";

    /// <summary>Serialized via <see cref="Json.JsonElementMemoryPackFormatter"/> - see its remarks.</summary>
    [MemoryPackAllowSerialize]
    public required JsonElement LayoutJson { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>Create/update request body for <c>/api/dashboards</c>.</summary>
/// <remarks>
/// <see cref="Description"/> is nullable rather than defaulted via a C# property
/// initializer for the same System.Text.Json source-gen/`required`-members reason
/// <see cref="SavedViewRequest.Description"/> is - see that type's remarks.
/// </remarks>
[MemoryPackable]
public sealed partial record DashboardRequest
{
    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Serialized via <see cref="Json.JsonElementMemoryPackFormatter"/> - see its remarks.</summary>
    [MemoryPackAllowSerialize]
    public required JsonElement LayoutJson { get; init; }
}

/// <summary>Response body for <c>GET /api/dashboards</c>.</summary>
[MemoryPackable]
public sealed partial record DashboardListResponse
{
    public required IReadOnlyList<Dashboard> Dashboards { get; init; }
}
