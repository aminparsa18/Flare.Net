# ADR-0023: Custom, user-built dashboards - storage shape and query model

Status: Accepted

Date: 2026-09-12

## Context

The roadmap's "Saved dashboards / shareable views" item was implemented
as `SavedView` (migration `0009_saved_views.sql`, `/api/views`): a
named, reloadable snapshot of *one* Explorer page's (Logs/Traces/
Metrics) filter state. `SavedView`'s own doc comment already flags the
gap this ADR closes:

> "the 'Saved dashboards / shareable views' roadmap item, scoped as
> saved-per-page filter state (not a multi-panel dashboard builder)."

The still-open roadmap line is explicit about the difference:

> "Custom, user-built dashboards (multi-panel, saved, composed from
> arbitrary log/trace/metric queries)."

This is a bigger feature - one saved object now holds *several*
independently-typed, independently-laid-out queries, each rendered as
its own widget - and shipping it well needs several PRs, not one. This
ADR settles the storage/query-model decisions up front so later phases
build on a single, deliberate shape instead of accreting one:

1. Is a dashboard's panel list a new table (one row per panel) or an
   opaque blob on the dashboard row (one row per dashboard)?
2. Is a panel's query stored as a real, typed C# object (`AlertRule`'s
   approach) or as opaque, dashboard-owned JSON (`SavedView`'s
   approach)?
3. Does a panel keep its own time range, or does the dashboard impose
   one on every panel?
4. Are dashboards global (like every `SavedView` today - no owner
   column, no Identity involvement) or per-user?

This ADR covers Phase 0 (design) and sets up Phase 1 (CRUD + a static
viewer) of the multi-phase plan; later ADRs or plain PR descriptions
can cover the editor (drag/resize layout), dashboard-wide time range,
and any templating/variables work, without re-litigating the shape
decided here.

## Decision

**One `dashboards` table, one row per dashboard, with an opaque
`LayoutJson` array of panels - `SavedView`'s shape, not `AlertRule`'s -
and panels keep their own independent query state exactly as
`SavedView.State` already does. Dashboards are global/unscoped, like
every other saved object in this codebase today.**

- **Storage**: `db/clickhouse/0020_dashboards.sql` (next after `0019`,
  the exception-count-alerting migration), a `ReplacingMergeTree`
  tombstone table identical in every mechanical respect to
  `saved_views` (`Id`, `Name`, `Description`, `IsDeleted`, `CreatedAt`,
  `UpdatedAt`) plus one column: `LayoutJson String CODEC(ZSTD(1))`,
  an opaque JSON array of panels. No `dashboard_panels` child table.
  Reasoning: nothing in `Flare.Api` ever needs to query or join on an
  individual panel - a dashboard is read and written as one whole
  object, the same "never interpreted server-side" reasoning
  `saved_views.StateJson`'s comment already gives for *its* opaque
  column. A child table would buy per-panel versioning/ordering at the
  SQL layer for a need nothing here has yet.
- **Models**: `Dashboard` / `DashboardRequest` / `DashboardListResponse`
  in a new `Flare.Api/Model/DashboardModels.cs`, `[MemoryPackable]`,
  mirroring `SavedView`/`SavedViewRequest`/`SavedViewListResponse`
  field-for-field with `LayoutJson` (a `JsonElement`, via
  `Json.JsonElementMemoryPackFormatter`) standing in for `State`. No
  `PageType` equivalent on the dashboard itself - a dashboard isn't
  scoped to one Explorer page, its panels are (see below).
- **Panel shape** (TypeScript-owned, round-tripped as opaque JSON, never
  a C# type - same category as `LogsFilterState`/`TracesFilterState`/
  `MetricsFilterState`): each array entry is `{ id, panelType: "Logs" |
  "Traces" | "Metrics", title, layout: { x, y, w, h }, query: <that
  panel type's existing *FilterState shape> }`. A panel's `query` is
  **exactly** the same object a `SavedView` of that `pageType` already
  stores - a panel is "embed this saved-search's live result as a
  widget." This is the load-bearing reuse decision: no new query DSL,
  no new panel-specific filter shape to design, and the existing
  "Pin to dashboard" action (Phase 1) can literally lift the current
  in-memory filter state off `LogsToolbar.svelte`/`MetricsToolbar.svelte`/
  the traces toolbar and drop it into a new panel unchanged.
- **Execution stays client-side, unchanged**: rendering a panel means
  the dashboard viewer calls the *same* endpoints the Explorer pages
  already call (`/api/logs/aggregate`, `/api/metrics/query` +
  `MetricSeriesQueryBuilder`, the traces query/aggregate endpoints)
  with that panel's stored `query`. No new "dashboard query execution"
  endpoint, no server-side panel renderer. `Flare.Api` never
  deserializes `LayoutJson` into a typed panel model any more than
  `SavedViewQueryService` deserializes `StateJson` today.
- **Time range**: v1 ships with each panel keeping its own complete
  filter state, time range included - identical to how a `SavedView`
  behaves today (loading one *replaces* the page's current time range).
  A dashboard-wide time-range picker that overrides every panel's own
  range is real, wanted, follow-on work, but it is a Phase 2 UI/state
  concern (does an override *replace* or *merge with* a panel's stored
  range; is the override itself persisted on the dashboard) - deferring
  it keeps this ADR from blocking Phase 1 on a decision Phase 1 doesn't
  need.
- **Ownership/scoping**: no owner column, no Identity/SQLite
  involvement - dashboards are visible to every authenticated user,
  same as `saved_views` and `alert_rules` today. `Flare.Identity`'s
  embedded SQLite is not touched by this feature.
- **API surface**: `IDashboardQueryService`/`DashboardQueryService`
  mirror `ISavedViewQueryService`/`SavedViewQueryService` method-for-
  method (`CreateAsync`/`ListAsync`/`GetAsync`/`UpdateAsync`/
  `DeleteAsync`, tombstone-insert on delete, `FROM dashboards FINAL
  WHERE IsDeleted = 0` reads). `Flare.Api/Endpoints/DashboardEndpoints.cs`
  mirrors `SavedViewEndpoints.cs`'s route shape under `/api/dashboards`.
  No `pageType` query-string filter (every `SavedViewQueryService.ListAsync`
  has one; a dashboard list has nothing equivalent to filter by).

## Alternatives considered

- **Normalized `dashboard_panels` child table** (one row per panel,
  foreign-keyed to `dashboards.Id`). Rejected for v1: it only pays off
  once something needs to query, reorder, or version panels
  independently at the SQL layer, and nothing does yet - a dashboard is
  always read and written whole. Revisit if a later phase needs
  per-panel history (e.g. "who last edited this one panel") the
  whole-row tombstone model can't give cheaply.
- **Typed panel-query union**, following `AlertRule.Condition`/
  `MetricCondition`/`ExceptionCondition`'s discriminated-union pattern
  instead of opaque JSON. Rejected for v1 for the same reason
  `SavedView.State` is opaque rather than typed: nothing in `Flare.Api`
  needs to *execute* a panel's query server-side today (execution is
  entirely client-driven, same as loading a saved view). A typed union
  becomes worth its cost the moment a real server-side execution need
  shows up - e.g. a PDF/image snapshot export, or "alert on this
  panel" - at which point it can follow `MetricAlertCondition`'s
  playbook (hand-written TypeScript companion in
  `$lib/memorypack/`, per ADR-0016, since nested filter types are
  `IReadOnlyList<T>`-generator-ineligible the same way `LogFilter`/
  `MetricFilter` already are).
- **Per-user dashboard ownership** now, ahead of any other object in
  this codebase having it. Rejected for v1: it's a real feature
  (private dashboards, "my dashboards" vs. "shared"), but introducing
  the *first* owned/scoped saved object here would mean designing that
  concept from scratch under this ADR's scope, not reusing an existing
  pattern. Ship global dashboards first, matching every other saved
  object; scope this to per-user later as its own decision if wanted,
  touching `Flare.Identity` deliberately rather than as a side effect.
- **Reusing `SavedView` itself** (a `Dashboards` `SavedViewPageType`
  value whose `State` holds an array of panels) instead of a new table/
  model pair. Rejected: `SavedViewPageType` is meant to mean "which
  Explorer page produced this state," and a dashboard isn't produced by
  a single page - conflating the two would make every future
  `SavedView`-specific assumption (one page type, one filter shape) a
  landmine for dashboard rows. A parallel, same-shaped table costs
  little and keeps both concepts honest.

## Consequences

- **Phase 1** (this ADR's immediate scope): migration `0020_dashboards.sql`
  (mirrored in `db/clickhouse-cluster/`), `DashboardModels.cs`,
  `IDashboardQueryService`/`DashboardQueryService`,
  `DashboardEndpoints.cs` under `/api/dashboards`, a hand-written
  `$lib/dashboards-api.ts` (same MemoryPack-content-negotiation
  convention as `saved-views-api.ts`), and a minimal `/dashboards` list
  + `/dashboards/[id]` viewer rendering panels in a fixed single-column
  stack (no drag/resize yet) - three panel types: metric chart (reuses
  `MetricChart.svelte`), log table/count, and traces. A "Pin to
  dashboard" action added to the existing Logs/Metrics/Traces toolbars
  is how panels actually get created in v1 - no hand-authored panel
  JSON in the UI.
- **Phase 2+** (not this ADR's scope, but shaped by it): drag/resize
  grid layout editing panel position/size within `LayoutJson`; a
  dashboard-wide time-range override; auto-refresh interval;
  duplicate/export. None of these require a `LayoutJson` schema
  migration beyond additive fields on the same opaque blob, since it's
  never interpreted server-side.
- A dashboard is exactly as safe as the query it embeds: every panel
  still executes through the existing per-domain query builders
  (`LogFilterSqlBuilder`, `MetricSeriesQueryBuilder`, the traces
  builders), which already set `QueryOptions.CustomSettings` execution
  caps - a dashboard introduces no new unbounded-query surface.
- `LayoutJson`'s panel shape is dashboard-TypeScript-owned and
  unversioned at the schema level, same tradeoff `StateJson` already
  accepts: a panel shape change is a frontend-only concern as long as
  it stays additive-compatible with already-saved dashboards (new
  optional fields only, same discipline `LogsFilterState` etc. already
  follow for `SavedView`).
- i18n and translated docs (fr/ru/zh-CN) for the new `/dashboards` UI
  are Phase 1 scope, not deferred - this codebase's convention is to
  ship them in the same PR as the user-visible feature.

## Related documentation

- `db/clickhouse/0009_saved_views.sql` - the schema and CRUD pattern
  this ADR's `dashboards` table copies mechanically.
- `docs-internal/adr/0016-memorypack-dashboard-typescript-adoption.md` -
  why nested filter types get hand-written TypeScript companions
  instead of `[GenerateTypeScript]`, relevant if/when panel queries
  ever become typed C# per this ADR's "Alternatives considered."
- `docs-internal/adr/0020-metric-threshold-alerting.md` and
  `docs-internal/adr/0022-exception-count-alerting.md` - the
  discriminated-union pattern this ADR deliberately does *not* adopt
  for panels in v1, and the pattern to follow if that changes later.
- `docs-internal/planning/roadmap.md` - "Custom, user-built dashboards"
  is the roadmap line this ADR implements the design for.
