# ADR-0025: Dashboard variables - structured filter targets, not textual substitution

Status: Accepted

Date: 2026-09-13

## Context

Phase 4 (part of the "Custom, user-built dashboards" roadmap item, see
`docs-internal/planning/roadmap.md`'s own git history) shipped a scoped-down
MVP: a single, fixed, always-present "Service" dropdown in a dashboard's
header, backed the same way each Explorer toolbar's own service filter
already is (a wide-window aggregate enumerating known service names),
narrowing every panel's `services` filter for the session. It was
deliberately not a real variable system - one hardcoded target, exactly one
of it, never persisted anywhere (not even its *existence* - every dashboard
got the same fixed dropdown).

The roadmap left three things explicitly open: a *saved, query-backed*
variable definition (not a fixed built-in), more than one variable per
dashboard, and letting a variable back something other than the one thing
"Service" covers. This ADR is that follow-up.

The natural reference point is Grafana's own template variables - `$var`
tokens substituted into a panel's *query text* before it's sent. That model
doesn't transplant directly: a Flare dashboard panel has no query text at
all. Each panel embeds one Explorer page's own typed, structured filter
state (`LogsSavedViewState`/`TracesSavedViewState`/`MetricsSavedViewState`)
and is rendered by mounting that Explorer's real state class + widget
wholesale (ADR-0023's "execution stays client-side, unchanged" decision) -
there is no query string for a `$var` token to be textually substituted
into, and introducing one just to support variables would undo that
decision for no other reason.

Three real decisions came up building this, each affecting more than one
file, worth recording:

1. Given there's no query text, what does a variable's selected value
   actually *do* to a panel?
2. Attribute filtering isn't uniform across panel types - Logs and Traces
   each have their own bag/key/value equality filter concept
   (`AttributeFilter`/`SpanAttributeFilter`), using different bag names
   (`Log`/`Resource`/`Scope` vs. `Span`/`Resource`/`Scope`), and Metrics has
   neither. How does one variable definition target the right bag on the
   right panel types without either fragmenting into per-panel-type
   variable kinds or silently doing the wrong thing on a mismatched panel?
3. With any number of variables now possible (not just one fixed
   time-range override plus one fixed service override), how do panel
   bodies apply "every currently active override" without the combinatorial
   pairwise "revert this one, reapply that one" logic Phase 2/4 already
   used for exactly two overrides?

## Decision

**A variable's value narrows a structured *target* (`Service` or
`Attribute`, not a textual token), one shared `DashboardAttributeBag` enum
scopes an attribute-target variable to whichever panel types can actually
express that bag, and panel bodies recompute their entire effective filter
from scratch on every override change instead of reverting/reapplying
pairwise.**

- **Targets, not tokens.** `DashboardVariable.target` is `'Service'` or
  `'Attribute'`. A `Service`-target variable's selected value(s) are passed
  to `explorer.setServices()` - the direct generalization of Phase 4's
  fixed override (any number of such variables now behave as an OR-list,
  since `setServices` already takes an array; last-write-wins was rejected
  as a needless footgun when "just union them" is free). An
  `Attribute`-target variable's value is *appended to* (never replacing) a
  panel's own saved `attributeFilters` - a panel that already filters on
  `level=error` and a dashboard variable both narrow the same panel
  together, not one silently overriding the other. Definitions
  (`DashboardVariable[]`) live in `DashboardLayout.variables`, persisted
  through `Dashboard.LayoutJson` exactly like panels - `Flare.Api` still
  never interprets this blob (ADR-0023's "opaque `JsonElement`" decision
  holds unchanged, so this needed zero backend/ClickHouse changes). Which
  value is *currently selected* stays session-only (`DashboardViewerState.
  variableValues`), same "never written back to the dashboard row" rule
  `timeRangeOverride` already established - a variable picked to answer one
  question during one session doesn't silently change what the next viewer
  sees by default.
- **One `DashboardAttributeBag` enum (`Log`/`Span`/`Resource`/`Scope`),
  applicability derived from it, not a separate flag.** Logs' own
  `AttributeBag` (`Log`/`Resource`/`Scope`) and Traces' own
  `SpanAttributeBag` (`Span`/`Resource`/`Scope`) already don't line up one
  bag scheme covers both entirely: `Log`/`Span` are panel-type-specific
  record bags (a log record's own attributes vs. a span's own), while
  `Resource`/`Scope` are the same ingest-time-correlated bag on both -
  the resource that emitted a log line is the same resource that emitted
  the span it's correlated with. Rather than a `panelTypes: PanelType[]`
  field an author has to remember to set correctly, applicability is
  *derived* from the bag itself and can't drift out of sync with it: `Log`
  variables apply only to Logs panels, `Span` only to Traces panels,
  `Resource`/`Scope` to both (`attributesForLogsPanel`/
  `attributesForTracesPanel` in `$lib/dashboards/variables.ts` do this
  filtering; Metrics panels never read `variableOverrides.attributes` at
  all, the same way they already ignore every attribute concept Logs/Traces
  have that Metrics doesn't). A `Query`-sourced variable's value list is
  resolved against Logs' `/api/logs/attribute-values` for `Log`/`Resource`/
  `Scope` and Traces' `/api/spans/attribute-values` for `Span` - no new API
  endpoint, both already exist to back `AttributeFiltersRow`/
  `SpanAttributeFiltersRow`'s own value-autocomplete.
- **Recompute the whole effective filter from scratch on every override
  change, not pairwise revert-and-reapply.** Phase 2/4's panel bodies each
  tracked "was override X active last run" per override and, on a revert,
  called `applySavedViewState` (restoring the panel's saved baseline) then
  manually reapplied whichever *other* override was still active - workable
  for exactly two overrides, but doesn't generalize to N variables without
  either an O(N) reapplication dance or a dependency-tracking system to
  decide what to reapply. Instead, one effect per panel body now depends on
  `(timeRangeOverride, variableOverrides)` as a whole and, on any change,
  unconditionally: restores the panel's saved baseline
  (`applySavedViewState(query)`), then layers the time-range override (if
  any), then `services` (if any variable resolved one), then the merged
  attribute filters (if any). This trades a small amount of redundant work
  (a full reset even when only one override actually changed, and - for
  Metrics specifically - an extra metric-name-list reload every time, since
  its `applySavedViewState` is async and reloads that list first) for a
  rule that can't drop a still-active override and needs no per-override
  bookkeeping as the variable count grows.

## Consequences

- No ClickHouse migration, no `Flare.Api` change - `LayoutJson` gained one
  more array (`variables`, alongside `panels`), still round-tripped opaquely.
  `DashboardLayout.parseLayout()` defaults a missing `variables` field to
  `[]` so every dashboard saved before this ADR keeps loading unchanged.
- **Phase 4's fixed, always-on "Service" dropdown is gone.** A dashboard
  that relied on it gets no variable dropdown at all until someone opens
  **Variables** and adds a `Service`-target one - a few seconds of setup,
  in exchange for it now being named, optional, one-of-any-number, and
  actually saved. This is a deliberate, visible behavior change, called out
  here and in `docs/how-to/build-custom-dashboards.md` rather than
  auto-migrated, since a Service variable's *name* and *default value* are
  choices this ADR has no principled way to make on an author's behalf.
- Every `Dashboard*PanelBody.svelte` was rewritten around the "recompute
  from scratch" rule above - each is simpler (no per-override
  `wasActive` flags) but issues one extra redundant fetch (immediately
  aborted by the next call in the same synchronous block, same
  already-accepted pattern Phase 4 used for its own two-override case) per
  override change.
- **Variable chaining (one variable's options narrowing based on another's
  selection) was explicitly not built here** - each variable's
  `Query`-sourced options resolved independently, against the same fixed
  wide (7-day) window every other "enumerate known values" query in this
  codebase already uses, not scoped to any other variable's current
  selection or to the dashboard's own time-range override. Built as a
  follow-up in [`0026-dashboard-variable-chaining.md`](0026-dashboard-variable-chaining.md) -
  chaining still resolves against this same 7-day window, just additionally
  narrowed by the parent's selected value.
- **Per-panel opt-out/opt-in for a variable doesn't exist** - like the
  time-range override before it, a variable narrows *every* panel its
  target/bag applies to, dashboard-wide. A panel that shouldn't be narrowed
  by a particular variable has no way to say so today.

## Related documentation

- `docs-internal/adr/0023-custom-dashboards.md` - the storage/query-model
  ADR this one builds on without changing.
- `docs-internal/adr/0024-custom-dashboards-phase2-editor.md` - established
  the "session-only override, never written to `LayoutJson`" precedent this
  ADR extends from one fixed override to any number of named variables.
- `docs/how-to/build-custom-dashboards.md` - the user-facing how-to,
  updated in the same PR as this ADR per this repo's documentation
  convention.
