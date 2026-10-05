using MemoryPack;
using System.Text.Json;

namespace Flare.Api.Model;

/// <summary>Which dashboard explorer page a <see cref="SavedView"/> belongs to.</summary>
public enum SavedViewPageType
{
    Logs,
    Traces,
    Metrics,

    /// <summary>A trace funnel's steps (the <c>/traces/funnels</c> page) - appended last so existing ordinals keep their meaning. See ADR-0067.</summary>
    Funnels,
}

/// <summary>
/// A named, reloadable snapshot of one dashboard page's filter/selection state - the
/// "Saved dashboards / shareable views" roadmap item, scoped as saved-per-page filter
/// state (not a multi-panel dashboard builder).
/// </summary>
/// <remarks>
/// <see cref="State"/> is kept as an opaque <see cref="JsonElement"/> deliberately - unlike
/// <see cref="AlertRule.Condition"/> (a real C# <see cref="LogFilter"/>), the shape here
/// (<c>LogsFilterState</c>/<c>TracesFilterState</c>/a Metrics equivalent) is owned
/// entirely by the dashboard's TypeScript. <see cref="Query.SavedViewQueryService"/> only
/// ever round-trips it through ClickHouse's opaque <c>StateJson</c> column - it's never
/// deserialized into a Flare.Api type, and no code here inspects its contents.
/// </remarks>
[MemoryPackable]
public sealed partial record SavedView
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = "";

    public required SavedViewPageType PageType { get; init; }

    /// <summary>Serialized via <see cref="Json.JsonElementMemoryPackFormatter"/> - see its remarks.</summary>
    [MemoryPackAllowSerialize]
    public required JsonElement State { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// The owning project (ADR-0123), or null for an instance-wide object (the default, and every
    /// row that predates <c>db/clickhouse/0052_project_id.sql</c>). On a request, null leaves an
    /// update's existing project untouched so a client that predates projects can't un-scope an
    /// object, and <see cref="Guid.Empty"/> clears it. Appended last - see the other appended members' versioning remarks.
    /// </summary>
    public Guid? ProjectId { get; init; }
}

/// <summary>Create/update request body for <c>/api/views</c>.</summary>
/// <remarks>
/// <see cref="Description"/> is nullable rather than defaulted via a C# property
/// initializer for the same reason <see cref="AlertRuleRequest.Description"/> is - see
/// that type's remarks for the full System.Text.Json source-gen/`required`-members
/// caveat. There's no <see cref="bool"/>/<see cref="int"/> member here to hit the sharper
/// "omitted vs. explicitly false/zero" edge of that caveat, but the same nullable-and-
/// coalesce-in-the-service shape is used anyway for consistency with the rest of this
/// API's request DTOs.
/// </remarks>
[MemoryPackable]
public sealed partial record SavedViewRequest
{
    public required string Name { get; init; }

    public string? Description { get; init; }

    public required SavedViewPageType PageType { get; init; }

    /// <summary>Serialized via <see cref="Json.JsonElementMemoryPackFormatter"/> - see its remarks.</summary>
    [MemoryPackAllowSerialize]
    public required JsonElement State { get; init; }

    /// <summary>
    /// The owning project (ADR-0123), or null for an instance-wide object (the default, and every
    /// row that predates <c>db/clickhouse/0052_project_id.sql</c>). On a request, null leaves an
    /// update's existing project untouched so a client that predates projects can't un-scope an
    /// object, and <see cref="Guid.Empty"/> clears it. Appended last - see the other appended members' versioning remarks.
    /// </summary>
    public Guid? ProjectId { get; init; }
}

/// <summary>Response body for <c>GET /api/views</c>.</summary>
[MemoryPackable]
public sealed partial record SavedViewListResponse
{
    public required IReadOnlyList<SavedView> Views { get; init; }
}
