# ADR-0054: Collapsible dashboard rows as separate grids

Status: Accepted (extends ADR-0024)

Date: 2026-09-25

## Context

A dashboard is one flat gridstack grid ([ADR-0024](0024-custom-dashboards-phase2-editor.md)).
Large dashboards become hard to navigate, and every panel still mounts (and,
once scrolled into view, queries) even when nobody cares about that part of
the dashboard right now. Grafana and SigNoz both solve this with named,
collapsible rows that own the panels beneath them
([signoz#4806](https://github.com/SigNoz/signoz/commit/191d9b0648bc084cf0d4adbfc00ca1238721cf90)),
showing a panel count on a collapsed row
([signoz#5822](https://github.com/SigNoz/signoz/commit/afc97511af366bb6f78408a30c420ade573b6610)).

Grafana models a row as a special panel inside the one grid: a row "owns"
whichever panels sit between it and the next row by `y` position, and a
collapsed row stashes its children in its own nested `panels` array. That
works in Grafana because its grid engine understands rows. Gridstack
doesn't. Faking it would mean recomputing ownership from positions on every
drag, and moving panels in and out of the grid on every collapse, fighting
the "Svelte owns add/remove, gridstack only reports position" split
ADR-0024 relies on.

## Decision

**Each row is its own gridstack instance.** The stored layout
(`DashboardLayout`, still opaque JSON to Flare.Api, so no migration or
backend change) gains:

- `rows?: { id, title, collapsed? }[]`: rows in display order.
- `DashboardPanel.rowId?`: the owning row. Absent, `null`, or naming a
  row that no longer exists means the **ungrouped area**, which renders
  above every row. That is where every panel on a pre-rows dashboard
  already is, so old layouts need no conversion.
- A panel's `layout.y` is relative to its own section's grid, not the
  whole dashboard.

A collapsed row simply doesn't render its grid. Its panels never mount, so
they never query. That's stronger than the existing lazy-load-on-scroll
gating, which still applies inside an expanded row.

Consequences of choosing separate grids:

- **A panel can't be dragged between rows.** Gridstack can drag between
  grids (`acceptWidgets`), but that moves DOM nodes out from under Svelte's
  keyed `{#each}`, exactly the ownership fight ADR-0024 avoids. Instead,
  edit mode gives each panel a "Move to row" menu. The panel keeps its size
  and lands below the target row's existing panels.
- **Rows are reordered with up/down buttons,** not by dragging, for the same
  reason.
- **Removing a row never removes panels.** Its panels move to the ungrouped
  area, below what's already there, keeping their relative arrangement.

**Collapse state:** `row.collapsed` is the default everyone sees on open.
Toggling a row in view mode is session-only, while in edit mode the toggle
is also saved. This follows the rule every other dashboard control already
follows: view mode never changes the saved dashboard (the time-range
override, variable selections, and auto-refresh are all session-only), and
it lets someone who can't edit the dashboard still fold rows away.

## Not done

- Grafana import still flattens rows (`grafana-import.ts` drops `row`
  panels and keeps their children). Mapping them onto Flare rows is
  possible now that rows exist, but it needs per-row `y` rebasing and
  wasn't part of this change.
- Nested rows. Neither Grafana nor SigNoz has them.
