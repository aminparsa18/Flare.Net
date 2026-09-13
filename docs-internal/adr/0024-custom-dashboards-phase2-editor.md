# ADR-0024: Custom dashboards Phase 2 - drag/resize editor, in-place panel authoring, session-only time-range override

Status: Accepted

Date: 2026-09-13

## Context

ADR-0023 shipped Phase 1: a dashboard is a named, global object holding
an opaque `LayoutJson` array of panels, each panel exactly one
Logs/Traces/Metrics saved-search query rendered as a widget. Panels could
only be added by pinning from an Explorer page's toolbar, always landed
full-width in a fixed single-column stack (no drag/resize), had no
in-place rename, and there was no way to view every panel over one
common time window. ADR-0023 explicitly deferred all of this to "Phase
2+", flagging it needs no `LayoutJson` schema change - `layout: {x,y,w,h}`
was already part of the panel shape, just unused by the Phase 1 viewer.

This ADR is the Phase 2 follow-up: a real drag/resize grid editor,
add/rename/remove/reposition/resize panels, a panel type ("Traces") that
turned out to already ship in Phase 1 (no work needed there), and a
dashboard-wide time-range override. Three real decisions came up
building it, each affecting more than one file, worth recording:

1. What renders the grid - a Svelte-native drag/resize library, or a
   framework-agnostic one wrapped by hand?
2. How does "add a panel" work from inside the dashboard editor, given
   ADR-0023 deliberately kept all query composition on the Explorer
   pages themselves?
3. Does the dashboard-wide time-range override get saved to the
   dashboard, or is it session-only?

## Decision

**gridstack.js (not a Svelte-native grid library), "Add panel" reuses
each Explorer page's individual filter controls (not its full toolbar
component), and the time-range override is session-only, never written
to `LayoutJson`.**

- **Grid library: gridstack.js 13.x.** Evaluated against
  `svelte-grid-extended`, the natural Svelte-idiomatic choice - rejected
  because its `package.json` declares a Svelte 4 peer dependency, and
  this codebase is Svelte 5 runes-mode throughout (`svelte-best-practices`
  skill). gridstack is framework-agnostic, MIT-licensed, actively
  maintained, and wraps cleanly: `DashboardGrid.svelte` owns one
  `GridStack` instance and never fights Svelte for DOM ownership because
  add/remove is driven by a Svelte action (`use:gridItem`) hooked to each
  `.grid-stack-item`'s own mount/destroy, not by imperative
  `grid.addWidget`/`removeWidget` calls scattered elsewhere - the *only*
  thing any other code does is push/filter the `panels` array, same
  "one direction of truth" discipline ADR-0023 already used for
  `LayoutJson` itself. Position/size is otherwise one-way *out* of
  gridstack: `panel.layout` seeds each item's initial `gs-x/y/w/h`
  attributes, and gridstack's `change` event (fired once per completed
  drag/resize, not per pixel) is the only path back into
  `DashboardViewerState.updateLayout`, which applies it optimistically
  before the PUT resolves (see `viewer.svelte.ts`'s own remarks) rather
  than waiting on a round-trip per drag.

  This still autosaves layout changes rather than requiring an explicit
  "Save" - `docs-internal/planning/roadmap.md`'s own notes on this
  feature flag a pitfall another OSS project (SigNoz) hit and walked
  back: wiring drag/resize straight to a persistence call on every
  *tick*, then removing that in favor of an explicit save. That's not
  what's happening here - gridstack's `change` fires once per completed
  interaction (drop/resize-end), not per intermediate pixel of movement,
  so this is "one write per deliberate move," the same cadence
  `removePanel`/`renamePanel`/`addPanel` already write on (all four
  predate and postdate this ADR with the same immediate-write
  convention, not tick-level spam). No dirty-state/explicit-save UI was
  introduced to keep that convention uniform across every panel
  mutation, not just layout.
- **"Add panel" reuses the underlying filter controls, not the toolbar
  shell.** `LogsToolbar.svelte`/`TracesToolbar.svelte`/`MetricsToolbar.svelte`
  each wire in far more than filtering - Export, Patterns, SavedSearchesMenu/
  ViewsMenu, ShareViewButton, `PinToDashboardButton` itself, the live-tail
  toggle, command-palette registration (`setActiveLogsExplorer`) - none of
  which belong in an authoring dialog, and embedding `PinToDashboardButton`
  inside "Add panel" would be recursive. Instead, three small components
  (`AddPanelLogsForm`/`AddPanelTracesForm`/`AddPanelMetricsForm`, under
  `lib/components/dashboards/add-panel/`) each create their own fresh,
  dialog-scoped explorer state + Svelte context (same isolation pattern
  `DashboardLogsPanelBody.svelte` etc. already use for *rendering* a
  panel, just now for *building* one) and compose the same underlying
  controls the real toolbars use - `TimeRangePicker`, `PopoverMultiSelect`,
  `MetricPicker`, the presets `Select` - plus a live preview
  (`VolumeChart`/`TraceList`/`MetricChart`) so what's shown is exactly
  what gets saved. `AddPanelDialog.svelte` mounts exactly one of the
  three at a time behind `{#if panelType === ...}`, not one shared
  component with dynamically-swapped context, because Svelte's
  `setContext` only works during a component's own initialization -
  switching types from a click handler on one long-lived instance isn't
  possible, but a fresh component instance per type, mounted/unmounted
  by the `{#if}`, gets a correct fresh `setContext` call for free every
  time.
- **Time-range override is session-only.** `DashboardViewerState.timeRangeOverride`
  lives only in that page's in-memory state, never sent back through
  `updateDashboard`/written to `LayoutJson`. Each `Dashboard*PanelBody.svelte`
  layers it on top of the panel's own saved range via
  `explorer.setTimeRangePreset(override)` and restores
  `applySavedViewState(query)` when the override clears, but the
  dashboard row itself never changes because of it. Rejected: persisting
  it (so it's remembered next time anyone opens the dashboard) - that
  would mean an override picked to answer one question during one
  session silently changes what every other viewer sees by default,
  which is a bigger, separate decision (whose default wins, does it
  need its own "save this view" action) not needed to ship the control
  itself. Fixed-duration presets only (`TIME_RANGE_PRESETS` minus
  `'custom'`) - the same set Traces'/Metrics' own toolbars already
  restrict themselves to - because `TracesExplorerState` has no
  `customRange` concept at all (see its own remarks), so an absolute
  custom range couldn't be honored uniformly across all three panel
  types anyway.

## Consequences

- No ClickHouse migration, no `Flare.Api` change - confirmed by ADR-0023's
  own prediction. `layout: {x,y,w,h}` was already part of the panel
  shape; this phase is the first to actually read/write non-default
  values for it.
- `PinToDashboardDialog.svelte` and `AddPanelDialog.svelte` share one
  placement helper (`nextPanelPosition` in `$lib/dashboards/layout.ts`)
  so a panel from either flow lands below existing panels rather than
  overlapping at `(0,0)`, and both now default to half-width (`w: 6`)
  instead of Phase 1's always-full-width (`w: 12`), so panels tile
  two-up by default.
- A dashboard has no "edit existing panel's query" action yet - only
  rename/reposition/resize/remove, plus adding a brand-new one. Changing
  an existing panel's filters still means removing it and adding (or
  pinning) a replacement. Not addressed here; a reasonable Phase 3
  candidate.
- Auto-refresh and duplicate/export, both named in ADR-0023's original
  "Phase 2+" bullet, are still not built - out of scope for this pass,
  left for a later one.

## Related documentation

- `docs-internal/adr/0023-custom-dashboards.md` - the storage/query-model
  ADR this one builds on without changing.
- `docs/how-to/build-custom-dashboards.md` - the user-facing how-to,
  updated in the same PR as this ADR per this repo's documentation
  convention.
